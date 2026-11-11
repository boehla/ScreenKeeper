using System.Windows;
using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Anfasser eines ausgewählten Zeichenobjekts: 8 Rahmen-Anfasser, die beiden Endpunkte einer Linie bzw. eines Pfeils
/// oder das Innere (verschieben).
/// </summary>
public enum ObjectHandle {
    None,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    Start,
    End,
    Move
}

/// <summary>
/// Bearbeiten von Zeichenobjekten in Bildpixeln (UI-frei): Umriss, Anfasser, Verschieben und Größe ändern.
/// Verschieben und Größe ändern liefern immer eine neue Kopie (das Original bleibt unverändert, für Rückgängig).
/// </summary>
public static class AnnotationEditing {
    /// <summary>Kleinste Schriftgröße bzw. Stempelgröße beim Größe-Ändern.</summary>
    public const double MinFontSize = 6;
    public const double MinStampSize = 8;

    /// <summary>Für „Größe ändern“ unbegrenzte Fläche (Objekte dürfen außerhalb des Bereichs liegen).</summary>
    public static readonly PixelRect Unbounded = new PixelRect(-1_000_000, -1_000_000, 2_000_000, 2_000_000);

    private static readonly ObjectHandle[] rectHandles = {
        ObjectHandle.TopLeft, ObjectHandle.TopRight, ObjectHandle.BottomRight, ObjectHandle.BottomLeft,
        ObjectHandle.Top, ObjectHandle.Right, ObjectHandle.Bottom, ObjectHandle.Left
    };
    private static readonly ObjectHandle[] cornerHandles = { ObjectHandle.TopLeft, ObjectHandle.TopRight, ObjectHandle.BottomRight, ObjectHandle.BottomLeft };
    private static readonly ObjectHandle[] lineHandles = { ObjectHandle.Start, ObjectHandle.End };

    /// <summary>
    /// Sichtbarer Umriss des Objekts in Bildpixeln (für Auswahlrahmen und Anfasser).
    /// </summary>
    public static Rect GetBounds(Annotation annotation, IAnnotationMeasure measure) {
        switch(annotation) {
            case RectangleAnnotation rectangle: return toRect(rectangle.Rect);
            case EllipseAnnotation ellipse: return toRect(ellipse.Rect);
            case HighlightAnnotation highlight: return toRect(highlight.Rect);
            case PixelateAnnotation pixelate: return toRect(pixelate.Rect);
            case BlurAnnotation blur: return toRect(blur.Rect);
            case LineAnnotation line: return withLineCaps(line);
            case ArrowAnnotation arrow: return withLineCaps(arrow);
            case FreehandAnnotation freehand: return withLineCaps(freehand);
            case TextAnnotation text: return measure.GetTextBounds(text);
            case NumberStampAnnotation stamp: return new Rect(stamp.X - stamp.Size / 2, stamp.Y - stamp.Size / 2, stamp.Size, stamp.Size);
            case CursorAnnotation cursor: return measure.GetCursorBounds(cursor);
        }
        return Rect.Empty;
    }

    /// <summary>
    /// Welche Anfasser das Objekt hat (Zeiger: keine, nur verschieben).
    /// </summary>
    public static IReadOnlyList<ObjectHandle> GetHandles(Annotation annotation) {
        switch(annotation) {
            case LineAnnotation:
            case ArrowAnnotation:
                return lineHandles;
            case NumberStampAnnotation:
                return cornerHandles;
            case CursorAnnotation:
            case UnknownAnnotation:
                return Array.Empty<ObjectHandle>();
        }
        return rectHandles;
    }

    /// <summary>
    /// Mittelpunkt eines Anfassers in kontinuierlichen Bildkoordinaten.
    /// </summary>
    public static Point GetHandlePoint(Annotation annotation, ObjectHandle handle, IAnnotationMeasure measure) {
        if(handle == ObjectHandle.Start || handle == ObjectHandle.End) {
            List<PixelPoint>? points = getLinePoints(annotation);
            if(points != null && points.Count >= 2) {
                PixelPoint point = points[handle == ObjectHandle.Start ? 0 : 1];
                return new Point(point.X + 0.5, point.Y + 0.5);
            }
        }
        Rect bounds = GetBounds(annotation, measure);
        double centerX = bounds.X + bounds.Width / 2;
        double centerY = bounds.Y + bounds.Height / 2;
        switch(handle) {
            case ObjectHandle.TopLeft: return new Point(bounds.Left, bounds.Top);
            case ObjectHandle.Top: return new Point(centerX, bounds.Top);
            case ObjectHandle.TopRight: return new Point(bounds.Right, bounds.Top);
            case ObjectHandle.Right: return new Point(bounds.Right, centerY);
            case ObjectHandle.BottomRight: return new Point(bounds.Right, bounds.Bottom);
            case ObjectHandle.Bottom: return new Point(centerX, bounds.Bottom);
            case ObjectHandle.BottomLeft: return new Point(bounds.Left, bounds.Bottom);
            case ObjectHandle.Left: return new Point(bounds.Left, centerY);
        }
        return new Point(centerX, centerY);
    }

    /// <summary>
    /// Anfasser unter (<paramref name="x"/>, <paramref name="y"/>); <paramref name="radius"/> = halbe Trefferfläche in Bildpixeln.
    /// </summary>
    public static ObjectHandle HitHandle(Annotation annotation, double x, double y, double radius, IAnnotationMeasure measure) {
        foreach(ObjectHandle handle in GetHandles(annotation)) {
            Point point = GetHandlePoint(annotation, handle, measure);
            if(Math.Abs(x - point.X) <= radius && Math.Abs(y - point.Y) <= radius) return handle;
        }
        return ObjectHandle.None;
    }

    public static EditorCursor GetCursor(ObjectHandle handle) {
        switch(handle) {
            case ObjectHandle.Start:
            case ObjectHandle.End: return EditorCursor.Cross;
            case ObjectHandle.Move: return EditorCursor.SizeAll;
            case ObjectHandle.None: return EditorCursor.Arrow;
        }
        return SelectionGeometry.GetCursor(toSelectionHandle(handle));
    }

    /// <summary>
    /// Kopie, um <paramref name="dx"/>/<paramref name="dy"/> Bildpixel verschoben.
    /// </summary>
    public static Annotation Moved(Annotation original, int dx, int dy) {
        Annotation copy = original.Clone();
        Offset(copy, dx, dy);
        return copy;
    }

    /// <summary>
    /// Verschiebt das Objekt (in place).
    /// </summary>
    public static void Offset(Annotation annotation, int dx, int dy) {
        switch(annotation) {
            case RectangleAnnotation rectangle: rectangle.Rect = rectangle.Rect.Offset(dx, dy); break;
            case EllipseAnnotation ellipse: ellipse.Rect = ellipse.Rect.Offset(dx, dy); break;
            case HighlightAnnotation highlight: highlight.Rect = highlight.Rect.Offset(dx, dy); break;
            case PixelateAnnotation pixelate: pixelate.Rect = pixelate.Rect.Offset(dx, dy); break;
            case BlurAnnotation blur: blur.Rect = blur.Rect.Offset(dx, dy); break;
            case LineAnnotation line: line.Points = offsetPoints(line.Points, dx, dy); break;
            case ArrowAnnotation arrow: arrow.Points = offsetPoints(arrow.Points, dx, dy); break;
            case FreehandAnnotation freehand: freehand.Points = offsetPoints(freehand.Points, dx, dy); break;
            case TextAnnotation text:
                text.X += dx;
                text.Y += dy;
                break;
            case NumberStampAnnotation stamp:
                stamp.X += dx;
                stamp.Y += dy;
                break;
            case CursorAnnotation cursor:
                cursor.X += dx;
                cursor.Y += dy;
                break;
        }
    }

    /// <summary>
    /// Kopie mit geänderter Größe: Anfasser <paramref name="handle"/> wurde bei Pixel <paramref name="down"/> gegriffen und
    /// steht jetzt bei <paramref name="pointer"/>. Rechteck-artige Objekte: Rahmen wie beim Bereich (klappt um);
    /// Freihand: Punkte werden skaliert; festes Textfeld: wie ein Rechteck (Schriftgröße bleibt); freier Text: seitliche
    /// Anfasser ändern die Umbruchbreite, die übrigen die Schriftgröße;
    /// Stempel: Durchmesser um den Mittelpunkt; Linie/Pfeil: Endpunkt (mit <paramref name="shift"/> im 45°-Raster).
    /// </summary>
    public static Annotation Resized(Annotation original, ObjectHandle handle, PixelPoint down, PixelPoint pointer, bool shift, IAnnotationMeasure measure) {
        Annotation copy = original.Clone();
        if(handle == ObjectHandle.None || handle == ObjectHandle.Move) return copy;
        switch(copy) {
            case RectangleAnnotation rectangle: rectangle.Rect = resizeRect(rectangle.Rect, handle, down, pointer); break;
            case EllipseAnnotation ellipse: ellipse.Rect = resizeRect(ellipse.Rect, handle, down, pointer); break;
            case HighlightAnnotation highlight: highlight.Rect = resizeRect(highlight.Rect, handle, down, pointer); break;
            case PixelateAnnotation pixelate: pixelate.Rect = resizeRect(pixelate.Rect, handle, down, pointer); break;
            case BlurAnnotation blur: blur.Rect = resizeRect(blur.Rect, handle, down, pointer); break;
            case FreehandAnnotation freehand: {
                PixelRect from = AnnotationGeometry.GetBounds(freehand.Points);
                PixelRect to = resizeRect(from, handle, down, pointer);
                freehand.Points = AnnotationGeometry.ScalePoints(freehand.Points, from, to);
                break;
            }
            case LineAnnotation line: line.Points = moveEndpoint(line.Points, handle, down, pointer, shift); break;
            case ArrowAnnotation arrow: arrow.Points = moveEndpoint(arrow.Points, handle, down, pointer, shift); break;
            case TextAnnotation text: resizeText(text, (TextAnnotation)original, handle, down, pointer, measure); break;
            case NumberStampAnnotation stamp: resizeStamp(stamp, handle, down, pointer); break;
        }
        return copy;
    }

    private static PixelRect resizeRect(PixelRect rect, ObjectHandle handle, PixelPoint down, PixelPoint pointer) {
        if(rect.IsEmpty) return rect;
        HandleDrag drag = HandleDrag.Begin(rect, toSelectionHandle(handle), down);
        return drag.Update(pointer, Unbounded).Rect;
    }

    private static List<PixelPoint> moveEndpoint(List<PixelPoint> points, ObjectHandle handle, PixelPoint down, PixelPoint pointer, bool shift) {
        List<PixelPoint> result = new List<PixelPoint>(points);
        if(result.Count < 2 || (handle != ObjectHandle.Start && handle != ObjectHandle.End)) return result;
        int index = handle == ObjectHandle.Start ? 0 : 1;
        PixelPoint other = result[1 - index];
        PixelPoint moved = new PixelPoint(result[index].X + pointer.X - down.X, result[index].Y + pointer.Y - down.Y);
        result[index] = shift ? AnnotationGeometry.SnapAngle(other, moved) : moved;
        return result;
    }

    private static void resizeText(TextAnnotation text, TextAnnotation original, ObjectHandle handle, PixelPoint down, PixelPoint pointer, IAnnotationMeasure measure) {
        if(TextLayout.IsField(original)) {
            // festes Feld: wie ein Rechteck in Breite und Höhe, die Schriftgröße bleibt (Auto passt sich über das Layout an)
            PixelRect field = resizeRect(new PixelRect(original.X, original.Y, original.Width!.Value, original.Height!.Value), handle, down, pointer);
            text.X = field.X;
            text.Y = field.Y;
            text.Width = Math.Max(1, field.Width);
            text.Height = Math.Max(1, field.Height);
            return;
        }
        Rect bounds = measure.GetTextBounds(original);
        PixelRect from = new PixelRect(original.X, original.Y, Math.Max(1, (int)Math.Ceiling(bounds.Width)), Math.Max(1, (int)Math.Ceiling(bounds.Height)));
        PixelRect to = resizeRect(from, handle, down, pointer);
        double padding = TextLayout.GetPadding(original.FontSize);
        if(handle == ObjectHandle.Left || handle == ObjectHandle.Right) {
            // seitlich: Umbruchbreite
            text.Width = (int)Math.Max(Math.Ceiling(original.FontSize), to.Width - 2 * padding);
            text.X = to.X;
            return;
        }
        double factor = to.Height / (double)from.Height;
        double fontSize = Math.Round(Math.Max(MinFontSize, original.FontSize * factor) * 2) / 2;
        double applied = fontSize / original.FontSize;
        text.FontSize = fontSize;
        if(original.Width.HasValue) text.Width = (int)Math.Max(Math.Ceiling(fontSize), Math.Round(original.Width.Value * applied));
        bool left = handle == ObjectHandle.TopLeft || handle == ObjectHandle.BottomLeft;
        bool top = handle == ObjectHandle.TopLeft || handle == ObjectHandle.Top || handle == ObjectHandle.TopRight;
        // gegenüberliegende Ecke bleibt (ungefähr, die neue Breite ergibt sich aus der Schrift) stehen
        text.X = left ? (int)Math.Round(from.Right - from.Width * applied) : original.X;
        text.Y = top ? (int)Math.Round(from.Bottom - from.Height * applied) : original.Y;
    }

    private static void resizeStamp(NumberStampAnnotation stamp, ObjectHandle handle, PixelPoint down, PixelPoint pointer) {
        // Abstand zwischen Mauspixel und Anfasser beim Greifen bleibt erhalten; der Mittelpunkt bleibt stehen
        double half = stamp.Size / 2;
        bool left = handle == ObjectHandle.TopLeft || handle == ObjectHandle.BottomLeft;
        bool top = handle == ObjectHandle.TopLeft || handle == ObjectHandle.TopRight;
        double cornerX = stamp.X + (left ? -half : half);
        double cornerY = stamp.Y + (top ? -half : half);
        double x = cornerX + pointer.X - down.X;
        double y = cornerY + pointer.Y - down.Y;
        double size = 2 * Math.Max(Math.Abs(x - stamp.X), Math.Abs(y - stamp.Y));
        stamp.Size = Math.Max(MinStampSize, Math.Round(size));
    }

    /// <summary>Umriss der Punkte, vergrößert um die Endsymbole (die z. B. als Punkt oder Dreieck über das Ende hinausragen).</summary>
    private static Rect withLineCaps(ILineCapAnnotation annotation) {
        Rect bounds = toRect(AnnotationGeometry.GetBounds(annotation.Points));
        if(annotation.Points.Count < 2) return bounds;
        (LineCapShape? start, LineCapShape? end) = AnnotationGeometry.GetLineCaps(annotation);
        if(start != null) bounds.Union(start.Bounds);
        if(end != null) bounds.Union(end.Bounds);
        return bounds;
    }

    private static List<PixelPoint>? getLinePoints(Annotation annotation) {
        switch(annotation) {
            case LineAnnotation line: return line.Points;
            case ArrowAnnotation arrow: return arrow.Points;
        }
        return null;
    }

    private static List<PixelPoint> offsetPoints(List<PixelPoint> points, int dx, int dy) {
        return points.Select(p => new PixelPoint(p.X + dx, p.Y + dy)).ToList();
    }

    private static SelectionHandle toSelectionHandle(ObjectHandle handle) {
        switch(handle) {
            case ObjectHandle.TopLeft: return SelectionHandle.TopLeft;
            case ObjectHandle.Top: return SelectionHandle.Top;
            case ObjectHandle.TopRight: return SelectionHandle.TopRight;
            case ObjectHandle.Right: return SelectionHandle.Right;
            case ObjectHandle.BottomRight: return SelectionHandle.BottomRight;
            case ObjectHandle.Bottom: return SelectionHandle.Bottom;
            case ObjectHandle.BottomLeft: return SelectionHandle.BottomLeft;
            case ObjectHandle.Left: return SelectionHandle.Left;
            case ObjectHandle.Move: return SelectionHandle.Move;
        }
        return SelectionHandle.None;
    }

    private static Rect toRect(PixelRect rect) {
        if(rect.IsEmpty) return new Rect(rect.X, rect.Y, Math.Max(0, rect.Width), Math.Max(0, rect.Height));
        return new Rect(rect.X, rect.Y, rect.Width, rect.Height);
    }
}
