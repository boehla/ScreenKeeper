using System.Windows;
using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Treffertest je Zeichenobjekt-Typ in kontinuierlichen Bildkoordinaten. <c>tolerance</c> ist ein Zuschlag in Bildpixeln
/// (vom Aufrufer aus Gerätepixeln umgerechnet), damit dünne Linien gut zu treffen sind.
/// Rahmen ohne Füllung zählen nur am Strich, Linien/Pfeile/Freihand über den Abstand zur Strecke,
/// Text, Stempel, Zeiger und Flächen (Textmarker, Verpixeln, Weichzeichnen) über ihre Fläche.
/// </summary>
public static class AnnotationHitTesting {
    /// <summary>
    /// Oberstes getroffenes Objekt (die Liste ist in Zeichenreihenfolge, also von hinten nach vorne durchsuchen).
    /// </summary>
    public static Annotation? FindTopmost(IReadOnlyList<Annotation> annotations, double x, double y, double tolerance, IAnnotationMeasure measure) {
        for(int i = annotations.Count - 1; i >= 0; i--) {
            if(HitTest(annotations[i], x, y, tolerance, measure)) return annotations[i];
        }
        return null;
    }

    public static bool HitTest(Annotation annotation, double x, double y, double tolerance, IAnnotationMeasure measure) {
        switch(annotation) {
            case RectangleAnnotation rectangle: return hitFrame(rectangle.Rect, rectangle.Thickness, !string.IsNullOrEmpty(rectangle.Fill), x, y, tolerance);
            case EllipseAnnotation ellipse: return hitEllipse(ellipse, x, y, tolerance);
            case LineAnnotation line: return hitPolyline(line.Points, line.Thickness, x, y, tolerance);
            case ArrowAnnotation arrow: return hitArrow(arrow, x, y, tolerance);
            case FreehandAnnotation freehand: return hitPolyline(freehand.Points, freehand.Thickness, x, y, tolerance);
            case HighlightAnnotation highlight: return hitArea(highlight.Rect, x, y, tolerance);
            case PixelateAnnotation pixelate: return hitArea(pixelate.Rect, x, y, tolerance);
            case BlurAnnotation blur: return hitArea(blur.Rect, x, y, tolerance);
            case TextAnnotation text: return contains(measure.GetTextBounds(text), x, y, tolerance);
            case NumberStampAnnotation stamp: return Math.Sqrt((x - stamp.X) * (x - stamp.X) + (y - stamp.Y) * (y - stamp.Y)) <= stamp.Size / 2 + tolerance;
            case CursorAnnotation cursor: return contains(measure.GetCursorBounds(cursor), x, y, tolerance);
        }
        return false;
    }

    /// <summary>
    /// Abstand des Punktes zum Umriss eines Rechtecks (innen und außen positiv).
    /// </summary>
    public static double DistanceToOutline(Rect rect, double x, double y) {
        double dx = Math.Max(Math.Max(rect.Left - x, 0), x - rect.Right);
        double dy = Math.Max(Math.Max(rect.Top - y, 0), y - rect.Bottom);
        if(dx > 0 || dy > 0) return Math.Sqrt(dx * dx + dy * dy);
        return Math.Min(Math.Min(x - rect.Left, rect.Right - x), Math.Min(y - rect.Top, rect.Bottom - y));
    }

    private static bool hitFrame(PixelRect pixels, double thickness, bool filled, double x, double y, double tolerance) {
        if(pixels.IsEmpty) return false;
        if(filled) return hitArea(pixels, x, y, tolerance);
        Rect stroke = AnnotationGeometry.GetStrokeRect(pixels, thickness);
        return DistanceToOutline(stroke, x, y) <= Math.Max(0.5, thickness / 2) + tolerance;
    }

    private static bool hitEllipse(EllipseAnnotation ellipse, double x, double y, double tolerance) {
        if(ellipse.Rect.IsEmpty) return false;
        Rect stroke = AnnotationGeometry.GetStrokeRect(ellipse.Rect, ellipse.Thickness);
        double rx = Math.Max(0.5, stroke.Width / 2);
        double ry = Math.Max(0.5, stroke.Height / 2);
        double dx = x - (stroke.X + stroke.Width / 2);
        double dy = y - (stroke.Y + stroke.Height / 2);
        double normalized = Math.Sqrt(dx * dx / (rx * rx) + dy * dy / (ry * ry));
        double halfStroke = Math.Max(0.5, ellipse.Thickness / 2);
        if(!string.IsNullOrEmpty(ellipse.Fill)) {
            double grownX = rx + halfStroke + tolerance;
            double grownY = ry + halfStroke + tolerance;
            return dx * dx / (grownX * grownX) + dy * dy / (grownY * grownY) <= 1;
        }
        // Abstand entlang des Strahls vom Mittelpunkt zum Umriss (Näherung, genau genug für die Auswahl)
        double distance = Math.Sqrt(dx * dx + dy * dy);
        double outline = normalized < 1e-9 ? Math.Min(rx, ry) : Math.Abs(distance - distance / normalized);
        return outline <= halfStroke + tolerance;
    }

    private static bool hitPolyline(IReadOnlyList<PixelPoint> pixels, double thickness, double x, double y, double tolerance) {
        if(pixels.Count == 0) return false;
        double limit = Math.Max(0.5, thickness / 2) + tolerance;
        Point previous = AnnotationGeometry.ToStrokePoint(pixels[0], thickness);
        if(pixels.Count == 1) return Math.Sqrt((x - previous.X) * (x - previous.X) + (y - previous.Y) * (y - previous.Y)) <= limit;
        for(int i = 1; i < pixels.Count; i++) {
            Point current = AnnotationGeometry.ToStrokePoint(pixels[i], thickness);
            if(AnnotationGeometry.DistanceToSegment(x, y, previous, current) <= limit) return true;
            previous = current;
        }
        return false;
    }

    private static bool hitArrow(ArrowAnnotation arrow, double x, double y, double tolerance) {
        if(hitPolyline(arrow.Points, arrow.Thickness, x, y, tolerance)) return true;
        if(arrow.Points.Count < 2) return false;
        double thickness = Math.Max(1, arrow.Thickness);
        Point start = AnnotationGeometry.ToStrokePoint(arrow.Points[0], thickness);
        Point tip = AnnotationGeometry.ToStrokePoint(arrow.Points[1], thickness);
        ArrowHead head = AnnotationGeometry.GetArrowHead(start, tip, thickness);
        if(AnnotationGeometry.IsInTriangle(x, y, head.Tip, head.Left, head.Right)) return true;
        return AnnotationGeometry.DistanceToSegment(x, y, head.Tip, head.Left) <= tolerance
            || AnnotationGeometry.DistanceToSegment(x, y, head.Left, head.Right) <= tolerance
            || AnnotationGeometry.DistanceToSegment(x, y, head.Right, head.Tip) <= tolerance;
    }

    private static bool hitArea(PixelRect rect, double x, double y, double tolerance) {
        if(rect.IsEmpty) return false;
        return contains(new Rect(rect.X, rect.Y, rect.Width, rect.Height), x, y, tolerance);
    }

    private static bool contains(Rect rect, double x, double y, double tolerance) {
        if(rect.IsEmpty) return false;
        return x >= rect.Left - tolerance && x <= rect.Right + tolerance && y >= rect.Top - tolerance && y <= rect.Bottom + tolerance;
    }
}
