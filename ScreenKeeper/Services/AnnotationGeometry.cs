using System.Windows;
using ScreenKeeper.Models;

namespace ScreenKeeper.Services;

/// <summary>Gefüllte Pfeilspitze: Spitze, die beiden hinteren Ecken und die Mitte der Grundlinie (dort endet der Schaft).</summary>
public readonly record struct ArrowHead(Point Tip, Point Left, Point Right, Point Base);

/// <summary>Kubischer Bézier-Abschnitt (der Startpunkt ist das Ende des vorigen Abschnitts).</summary>
public readonly record struct BezierSegment(Point Control1, Point Control2, Point End);

/// <summary>
/// Geometrie-Helfer für Zeichnen und Bearbeiten: Pfeilspitze, 45°-Raster, Glättung (Catmull-Rom → Bézier),
/// Ausdünnen von Freihand-Punkten, Abstände, Pixel-Ausrichtung von Strichen und Skalieren von Punkten.
/// Alle Koordinaten in Bildpixeln.
/// </summary>
public static class AnnotationGeometry {
    /// <summary>
    /// Versatz für Striche durch Pixelpunkte: Bei ungerader Strichstärke liegt die Linie auf der Pixelmitte, bei gerader
    /// auf der Pixelkante – so bleiben waagerechte und senkrechte Linien scharf.
    /// </summary>
    public static double GetStrokeOffset(double thickness) {
        int rounded = (int)Math.Round(Math.Max(1, thickness));
        return rounded % 2 == 1 ? 0.5 : 0;
    }

    /// <summary>
    /// Punkt eines Strichs für den Pixel <paramref name="pixel"/> (mit <see cref="GetStrokeOffset"/>).
    /// </summary>
    public static Point ToStrokePoint(PixelPoint pixel, double thickness) {
        double offset = GetStrokeOffset(thickness);
        return new Point(pixel.X + offset, pixel.Y + offset);
    }

    /// <summary>
    /// Mittellinie des Rahmens, damit der Strich vollständig innerhalb von <paramref name="rect"/> liegt
    /// (bei ganzzahliger Stärke fallen die Strichkanten genau auf Pixelkanten).
    /// </summary>
    public static Rect GetStrokeRect(PixelRect rect, double thickness) {
        double half = Math.Max(0, thickness) / 2;
        double insetX = Math.Min(half, rect.Width / 2.0);
        double insetY = Math.Min(half, rect.Height / 2.0);
        return new Rect(rect.X + insetX, rect.Y + insetY, Math.Max(0, rect.Width - 2 * insetX), Math.Max(0, rect.Height - 2 * insetY));
    }

    /// <summary>Länge der Pfeilspitze (proportional zur Strichstärke).</summary>
    public static double GetArrowHeadLength(double thickness) {
        return 4 * Math.Max(0.5, thickness) + 6;
    }

    /// <summary>Halbe Breite der Pfeilspitze.</summary>
    public static double GetArrowHeadHalfWidth(double thickness) {
        return 2 * Math.Max(0.5, thickness) + 3;
    }

    /// <summary>
    /// Pfeilspitze für einen Pfeil von <paramref name="start"/> nach <paramref name="tip"/>. Ist der Pfeil kürzer als die
    /// Spitze, wird die Spitze auf die Pfeillänge verkleinert.
    /// </summary>
    public static ArrowHead GetArrowHead(Point start, Point tip, double thickness) {
        Vector direction = tip - start;
        double length = direction.Length;
        if(length < 1e-6) {
            direction = new Vector(1, 0);
            length = 0;
        } else {
            direction /= length;
        }
        double headLength = GetArrowHeadLength(thickness);
        double halfWidth = GetArrowHeadHalfWidth(thickness);
        if(length < headLength) {
            double scale = Math.Max(0.3, length / headLength);
            headLength *= scale;
            halfWidth *= scale;
        }
        Vector normal = new Vector(-direction.Y, direction.X);
        Point basePoint = tip - direction * headLength;
        return new ArrowHead(tip, basePoint + normal * halfWidth, basePoint - normal * halfWidth, basePoint);
    }

    /// <summary>
    /// Shift beim Zeichnen: Endpunkt auf das 45°-Raster um <paramref name="start"/> legen (0°, 45°, 90° …).
    /// Waagerecht/senkrecht bleibt die Länge der Hauptrichtung, diagonal werden beide Achsen gleich lang.
    /// </summary>
    public static PixelPoint SnapAngle(PixelPoint start, PixelPoint end) {
        int dx = end.X - start.X;
        int dy = end.Y - start.Y;
        if(dx == 0 && dy == 0) return end;
        double angle = Math.Atan2(dy, dx);
        int octant = (int)Math.Round(angle / (Math.PI / 4));
        octant = ((octant % 8) + 8) % 8;
        switch(octant) {
            case 0:
            case 4:
                return new PixelPoint(end.X, start.Y);
            case 2:
            case 6:
                return new PixelPoint(start.X, end.Y);
        }
        int size = (int)Math.Round((Math.Abs(dx) + Math.Abs(dy)) / 2.0, MidpointRounding.AwayFromZero);
        int signX = octant == 1 || octant == 7 ? 1 : -1;
        int signY = octant == 1 || octant == 3 ? 1 : -1;
        return new PixelPoint(start.X + signX * size, start.Y + signY * size);
    }

    /// <summary>
    /// Glättung: Catmull-Rom-Spline durch alle Punkte, als kubische Bézier-Abschnitte (n Punkte → n − 1 Abschnitte).
    /// Die Kurve läuft durch jeden Punkt; Anfang und Ende werden durch Wiederholen des Randpunkts behandelt.
    /// </summary>
    public static IReadOnlyList<BezierSegment> SmoothPath(IReadOnlyList<Point> points) {
        List<BezierSegment> segments = new List<BezierSegment>();
        if(points.Count < 2) return segments;
        for(int i = 0; i < points.Count - 1; i++) {
            Point p0 = points[Math.Max(0, i - 1)];
            Point p1 = points[i];
            Point p2 = points[i + 1];
            Point p3 = points[Math.Min(points.Count - 1, i + 2)];
            Point c1 = p1 + (p2 - p0) / 6;
            Point c2 = p2 - (p3 - p1) / 6;
            segments.Add(new BezierSegment(c1, c2, p2));
        }
        return segments;
    }

    /// <summary>
    /// Freihand-Punkte ausdünnen (Ramer-Douglas-Peucker): Punkte, die höchstens <paramref name="tolerance"/> Pixel von der
    /// Verbindung ihrer Nachbarn abweichen, fallen weg. Erster und letzter Punkt bleiben immer; doppelte Punkte entfallen.
    /// </summary>
    public static List<PixelPoint> Simplify(IReadOnlyList<PixelPoint> points, double tolerance) {
        List<PixelPoint> unique = new List<PixelPoint>();
        foreach(PixelPoint point in points) {
            if(unique.Count == 0 || unique[unique.Count - 1] != point) unique.Add(point);
        }
        if(unique.Count <= 2) return unique;
        bool[] keep = new bool[unique.Count];
        keep[0] = true;
        keep[unique.Count - 1] = true;
        Stack<(int First, int Last)> ranges = new Stack<(int First, int Last)>();
        ranges.Push((0, unique.Count - 1));
        while(ranges.Count > 0) {
            (int first, int last) = ranges.Pop();
            if(last - first < 2) continue;
            Point a = new Point(unique[first].X, unique[first].Y);
            Point b = new Point(unique[last].X, unique[last].Y);
            double maxDistance = -1;
            int index = -1;
            for(int i = first + 1; i < last; i++) {
                double distance = DistanceToSegment(unique[i].X, unique[i].Y, a, b);
                if(distance > maxDistance) {
                    maxDistance = distance;
                    index = i;
                }
            }
            if(index >= 0 && maxDistance > tolerance) {
                keep[index] = true;
                ranges.Push((first, index));
                ranges.Push((index, last));
            }
        }
        List<PixelPoint> result = new List<PixelPoint>();
        for(int i = 0; i < unique.Count; i++) {
            if(keep[i]) result.Add(unique[i]);
        }
        return result;
    }

    /// <summary>
    /// Kürzester Abstand des Punktes (<paramref name="x"/>, <paramref name="y"/>) zur Strecke a–b.
    /// </summary>
    public static double DistanceToSegment(double x, double y, Point a, Point b) {
        double dx = b.X - a.X;
        double dy = b.Y - a.Y;
        double lengthSquared = dx * dx + dy * dy;
        double t = lengthSquared < 1e-12 ? 0 : Math.Clamp(((x - a.X) * dx + (y - a.Y) * dy) / lengthSquared, 0, 1);
        double nearestX = a.X + t * dx;
        double nearestY = a.Y + t * dy;
        return Math.Sqrt((x - nearestX) * (x - nearestX) + (y - nearestY) * (y - nearestY));
    }

    /// <summary>
    /// Liegt der Punkt im Dreieck a-b-c (Rand eingeschlossen)?
    /// </summary>
    public static bool IsInTriangle(double x, double y, Point a, Point b, Point c) {
        double d1 = cross(x, y, a, b);
        double d2 = cross(x, y, b, c);
        double d3 = cross(x, y, c, a);
        bool negative = d1 < 0 || d2 < 0 || d3 < 0;
        bool positive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(negative && positive);
    }

    /// <summary>
    /// Kleinstes Pixelrechteck, das alle Punkte enthält (Punkte inklusive). Leer bei keinem Punkt.
    /// </summary>
    public static PixelRect GetBounds(IReadOnlyList<PixelPoint> points) {
        if(points.Count == 0) return default;
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = int.MinValue;
        int bottom = int.MinValue;
        foreach(PixelPoint point in points) {
            left = Math.Min(left, point.X);
            top = Math.Min(top, point.Y);
            right = Math.Max(right, point.X);
            bottom = Math.Max(bottom, point.Y);
        }
        return new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// Punkte von einem Pixelrechteck in ein anderes abbilden (Ecken auf Ecken, Punkte inklusive).
    /// </summary>
    public static List<PixelPoint> ScalePoints(IReadOnlyList<PixelPoint> points, PixelRect from, PixelRect to) {
        List<PixelPoint> result = new List<PixelPoint>(points.Count);
        foreach(PixelPoint point in points) {
            result.Add(new PixelPoint(scale(point.X, from.X, from.Width, to.X, to.Width), scale(point.Y, from.Y, from.Height, to.Y, to.Height)));
        }
        return result;
    }

    private static int scale(int value, int fromStart, int fromSize, int toStart, int toSize) {
        if(fromSize <= 1) return toStart + (toSize - 1) / 2;
        double factor = (toSize - 1) / (double)(fromSize - 1);
        return toStart + (int)Math.Round((value - fromStart) * factor, MidpointRounding.AwayFromZero);
    }

    private static double cross(double x, double y, Point a, Point b) {
        return (x - b.X) * (a.Y - b.Y) - (a.X - b.X) * (y - b.Y);
    }
}
