using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Services.Native;

namespace ScreenKeeper.Services;

public interface IRenderService {
    /// <summary>
    /// Alle Zeichenobjekte des Dokuments als Zeichnung in Bildpixel-Koordinaten (ohne das Original selbst).
    /// </summary>
    DrawingGroup BuildAnnotationDrawing(CaptureDocument document, BitmapSource original);

    /// <summary>
    /// Wie <see cref="BuildAnnotationDrawing(CaptureDocument, BitmapSource)"/>, zeichnet aber nur Objekte, für die
    /// <paramref name="include"/> true liefert (z. B. ohne den gerade bearbeiteten Text).
    /// </summary>
    DrawingGroup BuildAnnotationDrawing(CaptureDocument document, BitmapSource original, Func<Annotation, bool>? include);

    /// <summary>
    /// Original + Zeichnungen, zugeschnitten auf <paramref name="region"/> (Pbgra32, 96 dpi, eingefroren).
    /// </summary>
    BitmapSource Render(CaptureDocument document, BitmapSource original, PixelRect region);
}

/// <summary>
/// Der eine Render-Pfad für Anzeige und Export: der Editor zeigt dasselbe Drawing an, das hier exportiert wird.
/// Je Annotation-Typ gibt es eine private draw…-Methode.
/// </summary>
public sealed class RenderService : IRenderService {
    /// <summary>Deckkraft des Textmarkers (Anteil an der gespeicherten, deckenden Farbe).</summary>
    public const double HighlightOpacity = 0.5;

    private static readonly Dictionary<string, CursorImage?> systemCursors = new Dictionary<string, CursorImage?>();
    private static readonly object cursorSync = new object();
    private static readonly Typeface stampTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Brush outlineBrush = freeze(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)));

    /// <summary>Schlüssel des Effekt-Zwischenspeichers: Art, Objekt, Rechteck (im Bild) und Blockgröße bzw. Radius.</summary>
    private readonly record struct EffectKey(bool Blur, string Id, PixelRect Rect, int Parameter);

    private readonly Dictionary<EffectKey, BitmapSource> effectCache = new Dictionary<EffectKey, BitmapSource>();
    private readonly HashSet<EffectKey> usedEffects = new HashSet<EffectKey>();
    private readonly object effectSync = new object();
    private BitmapSource? effectOriginal;
    private string? cachedCursorPng;
    private CursorImage? cachedCursor;

    /// <summary>Anzahl zwischengespeicherter Effekt-Bitmaps (für Tests).</summary>
    public int CachedEffectCount {
        get {
            lock(effectSync) {
                return effectCache.Count;
            }
        }
    }

    public DrawingGroup BuildAnnotationDrawing(CaptureDocument document, BitmapSource original) {
        return BuildAnnotationDrawing(document, original, null);
    }

    public DrawingGroup BuildAnnotationDrawing(CaptureDocument document, BitmapSource original, Func<Annotation, bool>? include) {
        DrawingGroup group = new DrawingGroup();
        lock(effectSync) {
            if(!ReferenceEquals(original, effectOriginal)) {
                effectCache.Clear();
                effectOriginal = original;
            }
            usedEffects.Clear();
        }
        using (DrawingContext dc = group.Open()) {
            foreach(Annotation annotation in document.Annotations) {
                if(include != null && !include(annotation)) continue;
                drawAnnotation(dc, document, original, annotation);
            }
        }
        // Nur die Effekte der aktuellen Zeichnung aufheben (beim Ziehen entstünden sonst beliebig viele)
        lock(effectSync) {
            List<EffectKey> unused = effectCache.Keys.Where(k => !usedEffects.Contains(k)).ToList();
            foreach(EffectKey key in unused) effectCache.Remove(key);
        }
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
        group.Freeze();
        return group;
    }

    public BitmapSource Render(CaptureDocument document, BitmapSource original, PixelRect region) {
        PixelRect imageBounds = new PixelRect(0, 0, original.PixelWidth, original.PixelHeight);
        PixelRect clipped = region.Intersect(imageBounds);
        if(clipped.IsEmpty) throw new ArgumentException($"Der Bereich {region} liegt außerhalb des Bildes ({imageBounds.Width} × {imageBounds.Height}).", nameof(region));

        DrawingGroup annotations = BuildAnnotationDrawing(document, original);
        DrawingVisual visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (DrawingContext dc = visual.RenderOpen()) {
            dc.PushTransform(new TranslateTransform(-clipped.X, -clipped.Y));
            // Original immer in Bildpixeln zeichnen, unabhängig von der dpi-Angabe des Bitmaps
            dc.DrawImage(original, new Rect(0, 0, original.PixelWidth, original.PixelHeight));
            dc.DrawDrawing(annotations);
            dc.Pop();
        }
        RenderTargetBitmap target = new RenderTargetBitmap(clipped.Width, clipped.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        target.Freeze();
        return target;
    }

    private void drawAnnotation(DrawingContext dc, CaptureDocument document, BitmapSource original, Annotation annotation) {
        switch(annotation) {
            case RectangleAnnotation rectangle: drawRectangle(dc, rectangle); break;
            case EllipseAnnotation ellipse: drawEllipse(dc, ellipse); break;
            case LineAnnotation line: drawLine(dc, line); break;
            case ArrowAnnotation arrow: drawArrow(dc, arrow); break;
            case FreehandAnnotation freehand: drawFreehand(dc, freehand); break;
            case TextAnnotation text: drawText(dc, text); break;
            case HighlightAnnotation highlight: drawHighlight(dc, highlight); break;
            case PixelateAnnotation pixelate: drawPixelate(dc, original, pixelate); break;
            case BlurAnnotation blur: drawBlur(dc, original, blur); break;
            case NumberStampAnnotation stamp: drawNumberStamp(dc, stamp); break;
            case CursorAnnotation cursor: drawCursor(dc, document, cursor); break;
            case UnknownAnnotation: break;
        }
    }

    private void drawRectangle(DrawingContext dc, RectangleAnnotation annotation) {
        if(annotation.Rect.IsEmpty) return;
        Brush? fill = string.IsNullOrEmpty(annotation.Fill) ? null : AnnotationColors.GetBrush(annotation.Fill);
        Pen? pen = createPen(annotation.Stroke, annotation.Thickness, PenLineCap.Square, PenLineJoin.Miter);
        if(fill == null && pen == null) return;
        dc.DrawRectangle(fill, pen, AnnotationGeometry.GetStrokeRect(annotation.Rect, pen == null ? 0 : annotation.Thickness));
    }

    private void drawEllipse(DrawingContext dc, EllipseAnnotation annotation) {
        if(annotation.Rect.IsEmpty) return;
        Brush? fill = string.IsNullOrEmpty(annotation.Fill) ? null : AnnotationColors.GetBrush(annotation.Fill);
        Pen? pen = createPen(annotation.Stroke, annotation.Thickness, PenLineCap.Round, PenLineJoin.Round);
        if(fill == null && pen == null) return;
        Rect rect = AnnotationGeometry.GetStrokeRect(annotation.Rect, pen == null ? 0 : annotation.Thickness);
        Point center = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
        dc.DrawEllipse(fill, pen, center, rect.Width / 2, rect.Height / 2);
    }

    private void drawLine(DrawingContext dc, LineAnnotation annotation) {
        if(annotation.Points.Count < 2 || annotation.Thickness <= 0 || double.IsNaN(annotation.Thickness)) return;
        drawCappedStroke(dc, annotation, annotation.Thickness, false);
    }

    private void drawArrow(DrawingContext dc, ArrowAnnotation annotation) {
        if(annotation.Points.Count < 2) return;
        drawCappedStroke(dc, annotation, Math.Max(1, annotation.Thickness), false);
    }

    private void drawFreehand(DrawingContext dc, FreehandAnnotation annotation) {
        if(annotation.Points.Count == 0) return;
        Pen? pen = createPen(annotation.Stroke, annotation.Thickness, PenLineCap.Round, PenLineJoin.Round);
        if(pen == null) return;
        if(annotation.Points.Count == 1) {
            Point point = AnnotationGeometry.ToStrokePoint(annotation.Points[0], annotation.Thickness);
            dc.DrawEllipse(pen.Brush, null, point, annotation.Thickness / 2, annotation.Thickness / 2);
            return;
        }
        drawCappedStroke(dc, annotation, annotation.Thickness, annotation.Smooth);
    }

    /// <summary>
    /// Linie, Pfeil oder Freihand mit Endsymbolen: zuerst der Strich (bei einer Pfeilspitze bis zu deren Grundlinie gekürzt,
    /// dort flach endend), dann die gefüllten Symbole in Strichfarbe darüber.
    /// </summary>
    private static void drawCappedStroke(DrawingContext dc, ILineCapAnnotation annotation, double thickness, bool smooth) {
        List<Point> points = AnnotationGeometry.GetStrokePoints(annotation, thickness);
        SolidColorBrush brush = AnnotationColors.GetBrush(annotation.Stroke);
        CappedStroke capped = AnnotationGeometry.GetCappedStroke(points, thickness, annotation.StartCap, annotation.EndCap, smooth);
        if(capped.TrimmedPath == null) {
            Pen pen = createPen(brush, thickness, PenLineCap.Round, PenLineCap.Round);
            drawPath(dc, pen, points, smooth);
        } else if(capped.TrimmedPath.Count >= 2) {
            PenLineCap startCap = capped.Start != null && capped.Start.TrimsLine ? PenLineCap.Flat : PenLineCap.Round;
            PenLineCap endCap = capped.End != null && capped.End.TrimsLine ? PenLineCap.Flat : PenLineCap.Round;
            drawPath(dc, createPen(brush, thickness, startCap, endCap), capped.TrimmedPath, false);
        }
        drawLineCap(dc, brush, capped.Start);
        drawLineCap(dc, brush, capped.End);
    }

    /// <summary>Linienzug (zwei Punkte als Linie, sonst als Streckenzug bzw. geglättete Kurve).</summary>
    private static void drawPath(DrawingContext dc, Pen pen, IReadOnlyList<Point> points, bool smooth) {
        if(points.Count == 2) {
            dc.DrawLine(pen, points[0], points[1]);
            return;
        }
        StreamGeometry geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open()) {
            context.BeginFigure(points[0], false, false);
            if(smooth && points.Count > 2) {
                foreach(BezierSegment segment in AnnotationGeometry.SmoothPath(points)) {
                    context.BezierTo(segment.Control1, segment.Control2, segment.End, true, true);
                }
            } else {
                context.PolyLineTo(points.Skip(1).ToList(), true, true);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);
    }

    private static void drawLineCap(DrawingContext dc, Brush brush, LineCapShape? shape) {
        if(shape == null) return;
        if(shape.IsCircle) {
            dc.DrawEllipse(brush, null, shape.Point, shape.Radius, shape.Radius);
            return;
        }
        if(shape.Polygon.Count < 3) return;
        StreamGeometry geometry = new StreamGeometry();
        using (StreamGeometryContext context = geometry.Open()) {
            context.BeginFigure(shape.Polygon[0], true, true);
            for(int i = 1; i < shape.Polygon.Count; i++) context.LineTo(shape.Polygon[i], true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
    }

    private static Pen createPen(Brush brush, double thickness, PenLineCap startCap, PenLineCap endCap) {
        Pen pen = new Pen(brush, thickness) {
            StartLineCap = startCap,
            EndLineCap = endCap,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    private void drawText(DrawingContext dc, TextAnnotation annotation) {
        if(string.IsNullOrEmpty(annotation.Text)) return;
        FormattedText formatted = TextLayout.Create(annotation, AnnotationColors.GetBrush(annotation.Foreground));
        double padding = TextLayout.GetPadding(annotation.FontSize);
        if(!string.IsNullOrEmpty(annotation.Background)) {
            Rect bounds = TextLayout.GetBounds(annotation, formatted);
            dc.DrawRoundedRectangle(AnnotationColors.GetBrush(annotation.Background), null, bounds, padding / 2, padding / 2);
        }
        dc.DrawText(formatted, new Point(annotation.X + padding, annotation.Y + padding));
    }

    private void drawHighlight(DrawingContext dc, HighlightAnnotation annotation) {
        if(annotation.Rect.IsEmpty) return;
        Color color = AnnotationColors.Parse(annotation.Color, Color.FromRgb(0xFF, 0xEB, 0x3B));
        SolidColorBrush brush = new SolidColorBrush(AnnotationColors.WithAlpha(color, (byte)Math.Round(color.A * HighlightOpacity)));
        brush.Freeze();
        dc.DrawRectangle(brush, null, toRect(annotation.Rect));
    }

    private void drawPixelate(DrawingContext dc, BitmapSource original, PixelateAnnotation annotation) {
        drawEffect(dc, original, annotation.Id, annotation.Rect, false, Math.Max(ImageEffects.MinBlockSize, annotation.BlockSize));
    }

    private void drawBlur(DrawingContext dc, BitmapSource original, BlurAnnotation annotation) {
        drawEffect(dc, original, annotation.Id, annotation.Rect, true, Math.Max(1, annotation.Radius));
    }

    private void drawNumberStamp(DrawingContext dc, NumberStampAnnotation annotation) {
        double size = Math.Max(4, annotation.Size);
        Point center = new Point(annotation.X, annotation.Y);
        Pen outline = new Pen(outlineBrush, Math.Max(1, size / 24));
        outline.Freeze();
        dc.DrawEllipse(AnnotationColors.GetBrush(annotation.Color), outline, center, size / 2, size / 2);
        string text = annotation.Number.ToString(CultureInfo.InvariantCulture);
        double factor = text.Length <= 1 ? 0.58 : text.Length == 2 ? 0.5 : 0.38;
        Brush foreground = AnnotationColors.IsLight(annotation.Color) ? Brushes.Black : Brushes.White;
        FormattedText formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, stampTypeface, size * factor, foreground, 1.0);
        // nach den tatsächlichen Glyphen zentrieren (nicht nach der Zeilenhöhe)
        Rect glyphs = formatted.BuildGeometry(new Point(0, 0)).Bounds;
        if(glyphs.IsEmpty) return;
        dc.DrawText(formatted, new Point(center.X - glyphs.X - glyphs.Width / 2, center.Y - glyphs.Y - glyphs.Height / 2));
    }

    /// <summary>
    /// Verpixeln/Weichzeichnen: Ergebnisbitmap aus dem Original berechnen (zwischengespeichert) und an die Stelle zeichnen.
    /// </summary>
    private void drawEffect(DrawingContext dc, BitmapSource original, string id, PixelRect rect, bool blur, int parameter) {
        PixelRect clipped = rect.Intersect(new PixelRect(0, 0, original.PixelWidth, original.PixelHeight));
        if(clipped.IsEmpty) return;
        EffectKey key = new EffectKey(blur, id, clipped, parameter);
        BitmapSource? bitmap;
        lock(effectSync) {
            usedEffects.Add(key);
            if(!effectCache.TryGetValue(key, out bitmap)) {
                bitmap = ComputeEffect(original, clipped, blur, parameter);
                effectCache[key] = bitmap;
            }
        }
        dc.DrawImage(bitmap, toRect(clipped));
    }

    /// <summary>
    /// Ergebnis von Verpixeln (<paramref name="blur"/> = false, Blockgröße) bzw. Weichzeichnen (Radius) für
    /// <paramref name="rect"/> (muss im Bild liegen), berechnet aus dem Original. Weichzeichnen liest zusätzlich einen Rand
    /// um das Rechteck, damit die Kanten natürlich verlaufen. Bgra32, eingefroren.
    /// </summary>
    public static BitmapSource ComputeEffect(BitmapSource original, PixelRect rect, bool blur, int parameter) {
        PixelRect imageBounds = new PixelRect(0, 0, original.PixelWidth, original.PixelHeight);
        PixelRect source = rect;
        if(blur) {
            int margin = ImageEffects.GetBlurMargin(parameter);
            source = new PixelRect(rect.X - margin, rect.Y - margin, rect.Width + 2 * margin, rect.Height + 2 * margin).Intersect(imageBounds);
        }
        byte[] pixels = copyBgra32(original, source);
        byte[] result;
        if(blur) {
            byte[] blurred = ImageEffects.Blur(pixels, source.Width, source.Height, parameter);
            result = ImageEffects.Crop(blurred, source.Width, source.Height, rect.X - source.X, rect.Y - source.Y, rect.Width, rect.Height);
        } else {
            result = ImageEffects.Pixelate(pixels, source.Width, source.Height, parameter);
        }
        BitmapSource bitmap = BitmapSource.Create(rect.Width, rect.Height, 96, 96, PixelFormats.Bgra32, null, result, rect.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] copyBgra32(BitmapSource original, PixelRect rect) {
        BitmapSource source = original.Format == PixelFormats.Bgra32 ? original : new FormatConvertedBitmap(original, PixelFormats.Bgra32, null, 0);
        int stride = rect.Width * 4;
        byte[] pixels = new byte[stride * rect.Height];
        source.CopyPixels(new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height), pixels, stride, 0);
        return pixels;
    }

    private static Pen? createPen(string stroke, double thickness, PenLineCap cap, PenLineJoin join) {
        if(thickness <= 0 || double.IsNaN(thickness)) return null;
        Pen pen = new Pen(AnnotationColors.GetBrush(stroke), thickness) {
            StartLineCap = cap,
            EndLineCap = cap,
            LineJoin = join
        };
        pen.Freeze();
        return pen;
    }

    private static Rect toRect(PixelRect rect) {
        return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }

    private void drawCursor(DrawingContext dc, CaptureDocument document, CursorAnnotation annotation) {
        if(!annotation.Visible) return;
        CursorImage? image = annotation.Kind == CursorKinds.Captured ? getCapturedCursor(document) : GetSystemCursor(annotation.Kind);
        if(image == null) return;
        Rect target = new Rect(annotation.X - image.HotspotX, annotation.Y - image.HotspotY, image.Bitmap.PixelWidth, image.Bitmap.PixelHeight);
        dc.DrawImage(image.Bitmap, target);
    }

    private CursorImage? getCapturedCursor(CaptureDocument document) {
        CursorInfo? cursor = document.Cursor;
        if(cursor == null || string.IsNullOrEmpty(cursor.Png)) return null;
        if(!ReferenceEquals(cursor.Png, cachedCursorPng) || cachedCursor == null || cachedCursor.HotspotX != cursor.HotspotX || cachedCursor.HotspotY != cursor.HotspotY) {
            try {
                BitmapSource bitmap = ImageFiles.DecodeBgra32(Convert.FromBase64String(cursor.Png));
                cachedCursor = new CursorImage(bitmap, cursor.HotspotX, cursor.HotspotY);
                cachedCursorPng = cursor.Png;
            } catch(Exception ex) {
                Log.Error(ex, "Aufgenommenes Zeigerbild konnte nicht gelesen werden");
                return null;
            }
        }
        return cachedCursor;
    }

    private static Brush freeze(Brush brush) {
        brush.Freeze();
        return brush;
    }

    /// <summary>
    /// Windows-Standardzeiger für <see cref="CursorKinds"/> (zwischengespeichert). Null bei unbekannter Art.
    /// </summary>
    public static CursorImage? GetSystemCursor(string kind) {
        lock(cursorSync) {
            if(systemCursors.TryGetValue(kind, out CursorImage? cached)) return cached;
            int id;
            switch(kind) {
                case CursorKinds.Arrow: id = User32.IDC_ARROW; break;
                case CursorKinds.Hand: id = User32.IDC_HAND; break;
                case CursorKinds.IBeam: id = User32.IDC_IBEAM; break;
                default: return null;
            }
            CursorImage? image = CursorImage.FromSystemCursor(id);
            systemCursors[kind] = image;
            return image;
        }
    }
}
