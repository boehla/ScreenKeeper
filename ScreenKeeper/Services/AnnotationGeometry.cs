using System.Windows;
using ScreenKeeper.Models;

namespace ScreenKeeper.Services;

/// <summary>Gefüllte Pfeilspitze: Spitze, die beiden hinteren Ecken und die Mitte der Grundlinie (dort endet der Schaft).</summary>
public readonly record struct ArrowHead(Point Tip, Point Left, Point Right, Point Base);

/// <summary>Kubischer Bézier-Abschnitt (der Startpunkt ist das Ende des vorigen Abschnitts).</summary>
public readonly record struct BezierSegment(Point Control1, Point Control2, Point End);

/// <summary>
/// Symbol an einem Linienende (siehe <see cref="LineCaps"/>) in Bildpixeln: <see cref="Point"/> ist der Endpunkt der Linie,
/// <see cref="Direction"/> die Richtung nach außen (Einheitsvektor). Gefüllt wird <see cref="Polygon"/> (Pfeil, Dreieck,
/// Quadrat) bzw. der Kreis mit <see cref="Radius"/> um den Endpunkt (Punkt). <see cref="LineEnd"/> ist der Punkt, an dem
/// der Strich endet: beim Pfeil die Mitte der Grundlinie (der Strich wird dorthin gekürzt), sonst der Endpunkt selbst.
/// </summary>
public sealed record LineCapShape(string Cap, Point Point, Vector Direction, IReadOnlyList<Point> Polygon, double Radius, Point LineEnd) {
    /// <summary>Wird der Strich an diesem Ende gekürzt (Pfeilspitze)?</summary>
    public bool TrimsLine => Cap == LineCaps.Arrow;

    public bool IsCircle => Cap == LineCaps.Dot;

    /// <summary>Umgebendes Rechteck des Symbols.</summary>
    public Rect Bounds {
        get {
            if(IsCircle) return new Rect(Point.X - Radius, Point.Y - Radius, 2 * Radius, 2 * Radius);
            Rect bounds = Rect.Empty;
            foreach(Point corner in Polygon) bounds.Union(corner);
            return bounds;
        }
    }

    /// <summary>Liegt der Punkt im Symbol oder höchstens <paramref name="tolerance"/> davon entfernt?</summary>
    public bool Contains(double x, double y, double tolerance) {
        if(IsCircle) return Math.Sqrt((x - Point.X) * (x - Point.X) + (y - Point.Y) * (y - Point.Y)) <= Radius + tolerance;
        if(AnnotationGeometry.IsInConvexPolygon(x, y, Polygon)) return true;
        for(int i = 0; i < Polygon.Count; i++) {
            if(AnnotationGeometry.DistanceToSegment(x, y, Polygon[i], Polygon[(i + 1) % Polygon.Count]) <= tolerance) return true;
        }
        return false;
    }
}

/// <summary>
/// Strich mit Endsymbolen: Symbole an Anfang und Ende (null = keins) und – nur wenn ein Ende gekürzt wird (Pfeilspitze) –
/// der gekürzte Linienzug (<see cref="TrimmedPath"/>, leer = kein Strich übrig). Ist <see cref="TrimmedPath"/> null, wird
/// der Strich unverändert gezeichnet.
/// </summary>
public sealed record CappedStroke(LineCapShape? Start, LineCapShape? End, IReadOnlyList<Point>? TrimmedPath);

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

    /// <summary>Radius des Punkt-Symbols am Linienende (proportional zur Strichstärke).</summary>
    public static double GetDotCapRadius(double thickness) {
        return 1.5 * Math.Max(0.5, thickness) + 2;
    }

    /// <summary>Halbe Kantenlänge des Quadrat-Symbols am Linienende.</summary>
    public static double GetSquareCapHalfSize(double thickness) {
        return 1.3 * Math.Max(0.5, thickness) + 2;
    }

    /// <summary>Umkreisradius des gleichseitigen Dreiecks am Linienende (Schwerpunkt auf dem Endpunkt).</summary>
    public static double GetTriangleCapRadius(double thickness) {
        return 2 * Math.Max(0.5, thickness) + 3.5;
    }

    /// <summary>
    /// Symbol <paramref name="cap"/> am Endpunkt <paramref name="point"/> mit der Richtung <paramref name="direction"/> nach
    /// außen. <paramref name="availableLength"/> ist die Länge, die die Pfeilspitze höchstens einnehmen darf (kürzere Linien
    /// bekommen eine kleinere Spitze, wie beim Pfeil). Null bei <see cref="LineCaps.None"/> und unbekannten Werten.
    /// Punkt, Quadrat und Dreieck liegen mittig auf dem Endpunkt, die Pfeilspitze endet mit ihrer Spitze dort.
    /// </summary>
    public static LineCapShape? GetLineCapShape(string? cap, Point point, Vector direction, double thickness, double availableLength) {
        if(direction.Length < 1e-9) direction = new Vector(1, 0);
        direction.Normalize();
        Vector normal = new Vector(-direction.Y, direction.X);
        switch(cap) {
            case LineCaps.Arrow: {
                ArrowHead head = GetArrowHead(point - direction * Math.Max(availableLength, 1e-3), point, thickness);
                return new LineCapShape(cap, point, direction, new[] { head.Tip, head.Left, head.Right }, 0, head.Base);
            }
            case LineCaps.Triangle: {
                double radius = GetTriangleCapRadius(thickness);
                Point baseCenter = point - direction * (radius / 2);
                double halfSide = radius * Math.Sqrt(3) / 2;
                Point[] corners = { point + direction * radius, baseCenter + normal * halfSide, baseCenter - normal * halfSide };
                return new LineCapShape(cap, point, direction, corners, 0, point);
            }
            case LineCaps.Square: {
                double half = GetSquareCapHalfSize(thickness);
                Vector along = direction * half;
                Vector across = normal * half;
                Point[] corners = { point + along + across, point - along + across, point - along - across, point + along - across };
                return new LineCapShape(cap, point, direction, corners, 0, point);
            }
            case LineCaps.Dot:
                return new LineCapShape(cap, point, direction, Array.Empty<Point>(), GetDotCapRadius(thickness), point);
        }
        return null;
    }

    /// <summary>
    /// Richtung nach außen am Anfang (<paramref name="atStart"/>) bzw. Ende eines Linienzugs (Einheitsvektor). Ohne Glättung
    /// aus den ersten bzw. letzten beiden verschiedenen Punkten; mit Glättung (<paramref name="smooth"/>, ab 3 Punkten) aus
    /// der Tangente der Kurve aus <see cref="SmoothPath"/>.
    /// </summary>
    public static Vector GetEndDirection(IReadOnlyList<Point> points, bool atStart, bool smooth) {
        Vector fallback = atStart ? new Vector(-1, 0) : new Vector(1, 0);
        if(points.Count < 2) return fallback;
        if(smooth && points.Count > 2) {
            IReadOnlyList<BezierSegment> segments = SmoothPath(points);
            Vector tangent;
            if(atStart) {
                BezierSegment first = segments[0];
                tangent = firstNonZero(points[0] - first.Control1, points[0] - first.Control2, points[0] - first.End);
            } else {
                BezierSegment last = segments[segments.Count - 1];
                Point before = segments.Count > 1 ? segments[segments.Count - 2].End : points[0];
                tangent = firstNonZero(last.End - last.Control2, last.End - last.Control1, last.End - before);
            }
            if(tangent.Length > 1e-9) {
                tangent.Normalize();
                return tangent;
            }
        }
        int count = points.Count;
        Point end = atStart ? points[0] : points[count - 1];
        for(int i = 1; i < count; i++) {
            Point other = atStart ? points[i] : points[count - 1 - i];
            Vector direction = end - other;
            if(direction.Length > 1e-9) {
                direction.Normalize();
                return direction;
            }
        }
        return fallback;
    }

    /// <summary>
    /// Strich-Punkte (<see cref="ToStrokePoint"/>) eines Objekts mit Linienenden: Linie und Pfeil nutzen die ersten beiden
    /// Punkte, Freihand alle.
    /// </summary>
    public static List<Point> GetStrokePoints(ILineCapAnnotation annotation, double thickness) {
        IEnumerable<PixelPoint> pixels = annotation is FreehandAnnotation ? annotation.Points : annotation.Points.Take(2);
        return pixels.Select(p => ToStrokePoint(p, thickness)).ToList();
    }

    /// <summary>
    /// Endsymbole eines Objekts (mit derselben Strichstärke und Glättung wie beim Zeichnen).
    /// </summary>
    public static (LineCapShape? Start, LineCapShape? End) GetLineCaps(ILineCapAnnotation annotation) {
        double thickness = Math.Max(1, annotation.Thickness);
        bool smooth = annotation is FreehandAnnotation freehand && freehand.Smooth;
        return GetLineCaps(GetStrokePoints(annotation, thickness), thickness, annotation.StartCap, annotation.EndCap, smooth);
    }

    /// <summary>
    /// Endsymbole eines Linienzugs (Strich-Punkte, siehe <see cref="ToStrokePoint"/>). Die Pfeilspitze darf höchstens so lang
    /// sein wie der Linienzug (bei Pfeilspitzen an beiden Enden je die Hälfte). Pfeilspitzen an Kurven richten sich nach der
    /// Sehne über die Spitzenlänge, die übrigen Symbole nach <see cref="GetEndDirection"/>.
    /// </summary>
    public static (LineCapShape? Start, LineCapShape? End) GetLineCaps(IReadOnlyList<Point> points, double thickness, string? startCap, string? endCap, bool smooth) {
        if(points.Count < 2) return (null, null);
        double length = GetPolylineLength(points);
        double available = startCap == LineCaps.Arrow && endCap == LineCaps.Arrow ? length / 2 : length;
        LineCapShape? start = getLineCap(startCap, points, true, thickness, available, smooth);
        LineCapShape? end = getLineCap(endCap, points, false, thickness, available, smooth);
        return (start, end);
    }

    /// <summary>
    /// Symbol an einem Ende. Punkt, Quadrat und Dreieck folgen der Richtung am Endpunkt (<see cref="GetEndDirection"/>).
    /// Eine Pfeilspitze an einem Linienzug mit mehr als zwei Punkten richtet sich nach der Sehne vom Endpunkt zu der Stelle,
    /// an der der Zug den Kreis mit der Spitzenlänge verlässt: Biegt sich die Kurve innerhalb der Spitze, säße die
    /// Grundlinie sonst neben dem Strich und es bliebe eine Lücke.
    /// </summary>
    private static LineCapShape? getLineCap(string? cap, IReadOnlyList<Point> points, bool atStart, double thickness, double available, bool smooth) {
        Point point = atStart ? points[0] : points[points.Count - 1];
        Vector direction = GetEndDirection(points, atStart, smooth);
        LineCapShape? shape = GetLineCapShape(cap, point, direction, thickness, available);
        if(shape == null || !shape.TrimsLine || points.Count <= 2) return shape;
        List<Point> path = smooth ? FlattenSmoothPath(points, 12) : points.ToList();
        if(atStart) path.Reverse();
        List<Point> trimmed = TrimPolylineEnd(path, (shape.Point - shape.LineEnd).Length);
        if(trimmed.Count == 0) return shape;
        Vector chord = point - trimmed[trimmed.Count - 1];
        return chord.Length > 1e-9 ? GetLineCapShape(cap, point, chord, thickness, available) : shape;
    }

    /// <summary>
    /// Strich mit Endsymbolen. Endet ein Ende in einer Pfeilspitze, wird der Linienzug (bei Glättung vorher in kurze
    /// Strecken zerlegt) bis zur Grundlinie der Spitze gekürzt und um höchstens einen Pixel in die Spitze verlängert, damit
    /// weder das Linienende heraussteht noch eine Lücke bleibt. Bleibt dabei höchstens ein halber Pixel übrig, entfällt der Strich.
    /// </summary>
    public static CappedStroke GetCappedStroke(IReadOnlyList<Point> points, double thickness, string? startCap, string? endCap, bool smooth) {
        (LineCapShape? start, LineCapShape? end) = GetLineCaps(points, thickness, startCap, endCap, smooth);
        bool trimStart = start != null && start.TrimsLine;
        bool trimEnd = end != null && end.TrimsLine;
        if(!trimStart && !trimEnd) return new CappedStroke(start, end, null);

        List<Point> path = smooth && points.Count > 2 ? FlattenSmoothPath(points, 12) : points.ToList();
        if(trimEnd) path = TrimPolylineEnd(path, (end!.Point - end.LineEnd).Length);
        if(trimStart && path.Count >= 2) {
            path.Reverse();
            path = TrimPolylineEnd(path, (start!.Point - start.LineEnd).Length);
            path.Reverse();
        }
        if(path.Count < 2 || GetPolylineLength(path) <= 0.5) return new CappedStroke(start, end, Array.Empty<Point>());
        double overlap = Math.Min(1, thickness / 2);
        if(trimEnd) path[path.Count - 1] = extend(path[path.Count - 2], path[path.Count - 1], overlap);
        if(trimStart) path[0] = extend(path[1], path[0], overlap);
        return new CappedStroke(start, end, path);
    }

    /// <summary>
    /// Geglättete Kurve (<see cref="SmoothPath"/>) als Linienzug mit <paramref name="stepsPerSegment"/> Strecken je Abschnitt.
    /// </summary>
    public static List<Point> FlattenSmoothPath(IReadOnlyList<Point> points, int stepsPerSegment) {
        List<Point> result = new List<Point>();
        if(points.Count == 0) return result;
        result.Add(points[0]);
        Point previous = points[0];
        foreach(BezierSegment segment in SmoothPath(points)) {
            for(int step = 1; step <= stepsPerSegment; step++) {
                double t = step / (double)stepsPerSegment;
                double u = 1 - t;
                double x = u * u * u * previous.X + 3 * u * u * t * segment.Control1.X + 3 * u * t * t * segment.Control2.X + t * t * t * segment.End.X;
                double y = u * u * u * previous.Y + 3 * u * u * t * segment.Control1.Y + 3 * u * t * t * segment.Control2.Y + t * t * t * segment.End.Y;
                result.Add(new Point(x, y));
            }
            previous = segment.End;
        }
        return result;
    }

    /// <summary>
    /// Kürzt einen Linienzug am Ende: Vom letzten Punkt rückwärts wird die Stelle gesucht, an der der Zug den Kreis mit
    /// Radius <paramref name="distance"/> um den letzten Punkt verlässt; dort endet das Ergebnis. Leer, wenn der ganze Zug
    /// im Kreis liegt.
    /// </summary>
    public static List<Point> TrimPolylineEnd(IReadOnlyList<Point> points, double distance) {
        List<Point> result = new List<Point>();
        if(points.Count < 2) return result;
        Point tip = points[points.Count - 1];
        if(distance <= 0) return points.ToList();
        for(int i = points.Count - 2; i >= 0; i--) {
            Point inner = points[i + 1];
            Point outer = points[i];
            if((outer - tip).Length < distance) continue;
            // Schnitt der Strecke inner→outer mit dem Kreis (inner liegt innen, outer außen oder auf dem Rand)
            Vector d = outer - inner;
            Vector f = inner - tip;
            double a = d * d;
            double b = 2 * (f * d);
            double c = f * f - distance * distance;
            double t = a < 1e-12 ? 1 : (-b + Math.Sqrt(Math.Max(0, b * b - 4 * a * c))) / (2 * a);
            for(int j = 0; j <= i; j++) result.Add(points[j]);
            result.Add(inner + d * Math.Clamp(t, 0, 1));
            return result;
        }
        return result;
    }

    /// <summary>Gesamtlänge eines Linienzugs.</summary>
    public static double GetPolylineLength(IReadOnlyList<Point> points) {
        double length = 0;
        for(int i = 1; i < points.Count; i++) length += (points[i] - points[i - 1]).Length;
        return length;
    }

    /// <summary>
    /// Liegt der Punkt in dem konvexen Vieleck (Rand eingeschlossen, Umlaufsinn beliebig)?
    /// </summary>
    public static bool IsInConvexPolygon(double x, double y, IReadOnlyList<Point> polygon) {
        if(polygon.Count < 3) return false;
        bool negative = false;
        bool positive = false;
        for(int i = 0; i < polygon.Count; i++) {
            double value = cross(x, y, polygon[i], polygon[(i + 1) % polygon.Count]);
            if(value < 0) negative = true;
            if(value > 0) positive = true;
        }
        return !(negative && positive);
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

    private static Vector firstNonZero(params Vector[] vectors) {
        foreach(Vector vector in vectors) {
            if(vector.Length > 1e-9) return vector;
        }
        return new Vector(0, 0);
    }

    /// <summary>Punkt <paramref name="end"/> um <paramref name="amount"/> in Richtung from→end verlängert.</summary>
    private static Point extend(Point from, Point end, double amount) {
        Vector direction = end - from;
        if(direction.Length < 1e-9) return end;
        direction.Normalize();
        return end + direction * amount;
    }
}
