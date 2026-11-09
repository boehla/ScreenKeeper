using ScreenKeeper.Models;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Teil des Bereichs unter dem Mauszeiger: einer der 8 Anfasser, das Innere (verschieben) oder nichts.
/// </summary>
public enum SelectionHandle {
    None,
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    Move
}

/// <summary>
/// Mauszeiger-Form, die ein Werkzeug für eine Position wünscht (die Ansicht setzt sie in WPF-Cursor um).
/// </summary>
public enum EditorCursor {
    Cross,
    Arrow,
    SizeAll,
    SizeNS,
    SizeWE,
    SizeNWSE,
    SizeNESW,
    Hand,
    IBeam
}

/// <summary>
/// UI-freie Bereichs-Logik in Bildpixeln: Rechteck aus zwei Pixeln, Begrenzung, Anfasser, Tastatur-Schritte, Monitor-Suche.
/// Pixelpunkte sind inklusive: ein Bereich von Pixel a bis Pixel b enthält beide.
/// </summary>
public static class SelectionGeometry {
    public const int LargeStep = 10;

    private static readonly SelectionHandle[] corners = { SelectionHandle.TopLeft, SelectionHandle.TopRight, SelectionHandle.BottomRight, SelectionHandle.BottomLeft };
    private static readonly SelectionHandle[] edges = { SelectionHandle.Top, SelectionHandle.Right, SelectionHandle.Bottom, SelectionHandle.Left };

    /// <summary>
    /// Normalisiertes Rechteck, das beide Pixel enthält (Reihenfolge egal).
    /// </summary>
    public static PixelRect FromPoints(PixelPoint a, PixelPoint b) {
        int left = Math.Min(a.X, b.X);
        int top = Math.Min(a.Y, b.Y);
        int right = Math.Max(a.X, b.X);
        int bottom = Math.Max(a.Y, b.Y);
        return new PixelRect(left, top, right - left + 1, bottom - top + 1);
    }

    /// <summary>
    /// Pixel auf das Bild begrenzen (0 … Breite-1 bzw. Höhe-1).
    /// </summary>
    public static PixelPoint ClampPoint(PixelPoint point, PixelRect bounds) {
        return new PixelPoint(Math.Clamp(point.X, bounds.X, bounds.Right - 1), Math.Clamp(point.Y, bounds.Y, bounds.Bottom - 1));
    }

    /// <summary>
    /// Rechteck auf das Bild zuschneiden; null, wenn nichts übrig bleibt.
    /// </summary>
    public static PixelRect? ClampRect(PixelRect rect, PixelRect bounds) {
        PixelRect clipped = rect.Intersect(bounds);
        return clipped.IsEmpty ? null : clipped;
    }

    /// <summary>
    /// Verschiebt das Rechteck um dx/dy, behält die Größe und bleibt vollständig im Bild.
    /// </summary>
    public static PixelRect MoveWithin(PixelRect rect, int dx, int dy, PixelRect bounds) {
        int width = Math.Min(rect.Width, bounds.Width);
        int height = Math.Min(rect.Height, bounds.Height);
        int x = Math.Clamp(rect.X + dx, bounds.X, bounds.Right - width);
        int y = Math.Clamp(rect.Y + dy, bounds.Y, bounds.Bottom - height);
        return new PixelRect(x, y, width, height);
    }

    /// <summary>
    /// Tastatur: Cursor um 1 Pixel (mit <paramref name="large"/> um 10) in Richtung dx/dy bewegen, im Bild bleiben.
    /// Liegt der Cursor außerhalb des Bildes, zählt der Schritt ab dem nächstgelegenen Bildpixel.
    /// </summary>
    public static PixelPoint MoveCursor(PixelPoint point, int dx, int dy, bool large, PixelRect bounds) {
        int step = large ? LargeStep : 1;
        PixelPoint start = ClampPoint(point, bounds);
        return ClampPoint(new PixelPoint(start.X + Math.Sign(dx) * step, start.Y + Math.Sign(dy) * step), bounds);
    }

    /// <summary>
    /// Mittelpunkt eines Anfassers in kontinuierlichen Bildkoordinaten (Kanten liegen zwischen den Pixeln).
    /// </summary>
    public static (double X, double Y) GetHandlePoint(PixelRect rect, SelectionHandle handle) {
        double centerX = rect.X + rect.Width / 2.0;
        double centerY = rect.Y + rect.Height / 2.0;
        switch(handle) {
            case SelectionHandle.TopLeft: return (rect.X, rect.Y);
            case SelectionHandle.Top: return (centerX, rect.Y);
            case SelectionHandle.TopRight: return (rect.Right, rect.Y);
            case SelectionHandle.Right: return (rect.Right, centerY);
            case SelectionHandle.BottomRight: return (rect.Right, rect.Bottom);
            case SelectionHandle.Bottom: return (centerX, rect.Bottom);
            case SelectionHandle.BottomLeft: return (rect.X, rect.Bottom);
            case SelectionHandle.Left: return (rect.X, centerY);
        }
        return (centerX, centerY);
    }

    /// <summary>Die 8 Anfasser (Ecken zuerst).</summary>
    public static IReadOnlyList<SelectionHandle> Handles { get; } = corners.Concat(edges).ToArray();

    /// <summary>
    /// Was liegt bei (x, y) (kontinuierliche Bildkoordinaten)? Reihenfolge: Ecken, Kantenmitten, Kantenlinien, Inneres.
    /// </summary>
    /// <param name="handleRadius">halbe Kantenlänge der Trefferfläche eines Anfassers in Bildpixeln</param>
    /// <param name="edgeTolerance">Abstand zur Kantenlinie in Bildpixeln, der noch als Kante zählt</param>
    public static SelectionHandle HitTest(PixelRect rect, double x, double y, double handleRadius, double edgeTolerance) {
        foreach(SelectionHandle handle in Handles) {
            (double hx, double hy) = GetHandlePoint(rect, handle);
            if(Math.Abs(x - hx) <= handleRadius && Math.Abs(y - hy) <= handleRadius) return handle;
        }
        bool withinX = x >= rect.X - edgeTolerance && x <= rect.Right + edgeTolerance;
        bool withinY = y >= rect.Y - edgeTolerance && y <= rect.Bottom + edgeTolerance;
        if(withinY && Math.Abs(x - rect.X) <= edgeTolerance) return SelectionHandle.Left;
        if(withinY && Math.Abs(x - rect.Right) <= edgeTolerance) return SelectionHandle.Right;
        if(withinX && Math.Abs(y - rect.Y) <= edgeTolerance) return SelectionHandle.Top;
        if(withinX && Math.Abs(y - rect.Bottom) <= edgeTolerance) return SelectionHandle.Bottom;
        if(x >= rect.X && x < rect.Right && y >= rect.Y && y < rect.Bottom) return SelectionHandle.Move;
        return SelectionHandle.None;
    }

    public static EditorCursor GetCursor(SelectionHandle handle) {
        switch(handle) {
            case SelectionHandle.TopLeft:
            case SelectionHandle.BottomRight: return EditorCursor.SizeNWSE;
            case SelectionHandle.TopRight:
            case SelectionHandle.BottomLeft: return EditorCursor.SizeNESW;
            case SelectionHandle.Top:
            case SelectionHandle.Bottom: return EditorCursor.SizeNS;
            case SelectionHandle.Left:
            case SelectionHandle.Right: return EditorCursor.SizeWE;
            case SelectionHandle.Move: return EditorCursor.SizeAll;
        }
        return EditorCursor.Cross;
    }

    /// <summary>
    /// Index des Monitors, der den Punkt enthält; liegt er außerhalb aller Monitore, der nächstgelegene. -1 ohne Monitore.
    /// </summary>
    public static int FindMonitorIndex(IReadOnlyList<MonitorInfo> monitors, PixelPoint point) {
        int best = -1;
        long bestDistance = long.MaxValue;
        for(int i = 0; i < monitors.Count; i++) {
            PixelRect bounds = monitors[i].Bounds;
            if(bounds.Contains(point.X, point.Y)) return i;
            long dx = point.X < bounds.X ? bounds.X - point.X : point.X >= bounds.Right ? point.X - bounds.Right + 1 : 0;
            long dy = point.Y < bounds.Y ? bounds.Y - point.Y : point.Y >= bounds.Bottom ? point.Y - bounds.Bottom + 1 : 0;
            long distance = dx * dx + dy * dy;
            if(distance < bestDistance) {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    /// <summary>
    /// Index des Monitors mit der größten Überdeckung mit <paramref name="rect"/>; ohne Überdeckung der zum Mittelpunkt nächste.
    /// </summary>
    public static int FindMonitorIndex(IReadOnlyList<MonitorInfo> monitors, PixelRect rect) {
        int best = -1;
        long bestArea = 0;
        for(int i = 0; i < monitors.Count; i++) {
            PixelRect overlap = monitors[i].Bounds.Intersect(rect);
            long area = (long)overlap.Width * overlap.Height;
            if(area > bestArea) {
                bestArea = area;
                best = i;
            }
        }
        if(best >= 0) return best;
        return FindMonitorIndex(monitors, new PixelPoint(rect.X + rect.Width / 2, rect.Y + rect.Height / 2));
    }

    /// <summary>
    /// Index des primären Monitors (sonst 0, ohne Monitore -1).
    /// </summary>
    public static int FindPrimaryMonitorIndex(IReadOnlyList<MonitorInfo> monitors) {
        for(int i = 0; i < monitors.Count; i++) {
            if(monitors[i].Primary) return i;
        }
        return monitors.Count > 0 ? 0 : -1;
    }
}
