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
    /// <paramref name="include"/> true liefert (z. B. ohne den gerade bearbeiteten Text). Ausgelassene Objekte zählen auch
    /// nicht zum Untergrund eines Textmarkers.
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
    /// <summary>Farbe des Textmarkers, wenn die gespeicherte Farbe nicht lesbar ist (Gelb, deckend).</summary>
    public static readonly Color DefaultHighlightColor = Color.FromArgb(0xFF, 0xFF, 0xEB, 0x3B);

    private static readonly Dictionary<string, CursorImage?> systemCursors = new Dictionary<string, CursorImage?>();
    private static readonly object cursorSync = new object();
    private static readonly Typeface stampTypeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
    private static readonly Brush outlineBrush = freeze(new SolidColorBrush(Color.FromArgb(0x66, 0, 0, 0)));

    /// <summary>Art eines zwischengespeicherten Effekts.</summary>
    private enum EffectKind {
        Pixelate,
        Blur,
        Highlight
    }

    /// <summary>
    /// Schlüssel des Effekt-Zwischenspeichers: Art, Objekt, Rechteck (im Bild) und Blockgröße, Radius bzw. Markerfarbe (ARGB).
    /// </summary>
    private readonly record struct EffectKey(EffectKind Kind, string Id, PixelRect Rect, int Parameter);

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
            // je Objekt eine eigene Teilzeichnung: der Textmarker braucht, was in der Reihenfolge vor ihm liegt
            List<Drawing> below = new List<Drawing>();
            foreach(Annotation annotation in document.Annotations) {
                if(include != null && !include(annotation)) continue;
                DrawingGroup part = new DrawingGroup();
                using (DrawingContext partContext = part.Open()) {
                    drawAnnotation(partContext, document, original, annotation, below);
                }
                if(part.Children.Count == 0) continue;
                RenderOptions.SetBitmapScalingMode(part, BitmapScalingMode.NearestNeighbor);
                part.Freeze();
                dc.DrawDrawing(part);
                below.Add(part);
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

    private void drawAnnotation(DrawingContext dc, CaptureDocument document, BitmapSource original, Annotation annotation, IReadOnlyList<Drawing> below) {
        switch(annotation) {
            case RectangleAnnotation rectangle: drawRectangle(dc, rectangle); break;
            case EllipseAnnotation ellipse: drawEllipse(dc, ellipse); break;
            case LineAnnotation line: drawLine(dc, line); break;
            case ArrowAnnotation arrow: drawArrow(dc, arrow); break;
            case FreehandAnnotation freehand: drawFreehand(dc, freehand); break;
            case TextAnnotation text: drawText(dc, text); break;
            case HighlightAnnotation highlight: drawHighlight(dc, original, highlight, below); break;
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
        TextArrangement layout = TextLayout.Arrange(annotation, AnnotationColors.GetBrush(annotation.Foreground));
        if(!string.IsNullOrEmpty(annotation.Background)) {
            dc.DrawRoundedRectangle(AnnotationColors.GetBrush(annotation.Background), null, layout.Bounds, layout.Padding / 2, layout.Padding / 2);
        }
        // festes Feld: was nicht hineinpasst, wird am Feldrand abgeschnitten (Anzeige und Export)
        if(layout.IsField) {
            RectangleGeometry clip = new RectangleGeometry(layout.Bounds);
            clip.Freeze();
            dc.PushClip(clip);
        }
        dc.DrawText(layout.Text, layout.Origin);
        if(layout.IsField) dc.Pop();
    }

    /// <summary>
    /// Textmarker im Modus „Multiplizieren“ (siehe <see cref="ImageEffects.Multiply"/>): Text darunter bleibt dunkel, Weiß wird
    /// zur Markerfarbe. Der Alpha-Wert der Farbe wirkt als Stärke. WPF kennt keine Mischmodi, daher wird das Ergebnis als
    /// Bitmap berechnet. Untergrund ist, was an der Stelle tatsächlich zu sehen ist:
    /// liegt keins der vorher gezeichneten Objekte im Rechteck, nur das Original (zwischengespeichert wie Verpixeln);
    /// sonst Original plus diese Objekte (Verpixelung, Rahmen, Stempel, frühere Marker …), damit z. B. unter einer Verpixelung
    /// nie das Original durchscheint. Dieser Fall wird nicht zwischengespeichert.
    /// </summary>
    private void drawHighlight(DrawingContext dc, BitmapSource original, HighlightAnnotation annotation, IReadOnlyList<Drawing> below) {
        PixelRect clipped = annotation.Rect.Intersect(new PixelRect(0, 0, original.PixelWidth, original.PixelHeight));
        if(clipped.IsEmpty) return;
        Color color = AnnotationColors.Parse(annotation.Color, DefaultHighlightColor);
        if(color.A == 0) return;
        Rect area = toRect(clipped);
        List<Drawing> covering = below.Where(d => overlaps(d.Bounds, area)).ToList();
        BitmapSource bitmap;
        if(covering.Count == 0) {
            int argb = color.A << 24 | color.R << 16 | color.G << 8 | color.B;
            bitmap = getEffect(new EffectKey(EffectKind.Highlight, annotation.Id, clipped, argb), () => ComputeHighlight(copyPixels(original, clipped, PixelFormats.Pbgra32), clipped, color));
        } else {
            bitmap = ComputeHighlight(renderBelow(original, covering, clipped), clipped, color);
        }
        dc.DrawImage(bitmap, area);
    }

    /// <summary>
    /// Textmarker-Ergebnis für <paramref name="rect"/> aus dem Untergrund <paramref name="pixels"/> (Pbgra32, Größe des Rechtecks).
    /// Pbgra32, eingefroren.
    /// </summary>
    public static BitmapSource ComputeHighlight(byte[] pixels, PixelRect rect, Color color) {
        byte[] result = ImageEffects.Multiply(pixels, rect.Width, rect.Height, color.R, color.G, color.B, color.A);
        BitmapSource bitmap = BitmapSource.Create(rect.Width, rect.Height, 96, 96, PixelFormats.Pbgra32, null, result, rect.Width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>
    /// Kann eine Zeichnung mit diesen Grenzen das Rechteck berühren? Großzügig (Kantenglättung reicht etwa ein Pixel hinaus).
    /// </summary>
    private static bool overlaps(Rect bounds, Rect area) {
        if(bounds.IsEmpty) return false;
        bounds.Inflate(1, 1);
        return bounds.IntersectsWith(area);
    }

    /// <summary>
    /// Original plus <paramref name="drawings"/> im Rechteck <paramref name="rect"/> (Bildpixel) als Pbgra32-Pixel.
    /// </summary>
    private static byte[] renderBelow(BitmapSource original, IReadOnlyList<Drawing> drawings, PixelRect rect) {
        DrawingVisual visual = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
        using (DrawingContext dc = visual.RenderOpen()) {
            dc.PushTransform(new TranslateTransform(-rect.X, -rect.Y));
            dc.DrawImage(original, new Rect(0, 0, original.PixelWidth, original.PixelHeight));
            foreach(Drawing drawing in drawings) dc.DrawDrawing(drawing);
            dc.Pop();
        }
        RenderTargetBitmap target = new RenderTargetBitmap(rect.Width, rect.Height, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        int stride = rect.Width * 4;
        byte[] pixels = new byte[stride * rect.Height];
        target.CopyPixels(pixels, stride, 0);
        return pixels;
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
        EffectKey key = new EffectKey(blur ? EffectKind.Blur : EffectKind.Pixelate, id, clipped, parameter);
        BitmapSource bitmap = getEffect(key, () => ComputeEffect(original, clipped, blur, parameter));
        dc.DrawImage(bitmap, toRect(clipped));
    }

    /// <summary>
    /// Effekt-Bitmap aus dem Zwischenspeicher oder neu berechnen; als in dieser Zeichnung benutzt vermerken.
    /// </summary>
    private BitmapSource getEffect(EffectKey key, Func<BitmapSource> compute) {
        lock(effectSync) {
            usedEffects.Add(key);
            if(!effectCache.TryGetValue(key, out BitmapSource? bitmap)) {
                bitmap = compute();
                effectCache[key] = bitmap;
            }
            return bitmap;
        }
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
        byte[] pixels = copyPixels(original, source, PixelFormats.Bgra32);
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

    /// <summary>Ausschnitt des Originals in <paramref name="format"/> (Bgra32 oder Pbgra32) kopieren.</summary>
    private static byte[] copyPixels(BitmapSource original, PixelRect rect, PixelFormat format) {
        BitmapSource source = original.Format == format ? original : new FormatConvertedBitmap(original, format, null, 0);
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
