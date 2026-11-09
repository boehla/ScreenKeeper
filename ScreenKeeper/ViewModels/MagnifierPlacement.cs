using System.Globalization;
using System.Windows;
using ScreenKeeper.Models;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// UI-freie Berechnungen für die Lupe: Größe, Ausweich-Position, Textzeile.
/// </summary>
public static class MagnifierPlacement {
    /// <summary>
    /// Anzahl sichtbarer Pixel je Seite: so viele, wie bei der Vergrößerung in die gewünschte Größe passen, immer ungerade
    /// (damit es einen Mittelpixel gibt), mindestens 3.
    /// </summary>
    public static int GetCellCount(double sizeDevicePixels, int zoom) {
        int count = Math.Max(3, (int)Math.Floor(sizeDevicePixels / Math.Max(1, zoom)));
        return count % 2 == 0 ? count + 1 : count;
    }

    /// <summary>
    /// Position (links oben) der Lupe: standardmäßig rechts unterhalb des Cursors mit Abstand <paramref name="gap"/>,
    /// bei Platzmangel links unten, rechts oben oder links oben – immer vollständig innerhalb von <paramref name="bounds"/>.
    /// </summary>
    public static Point Place(Point cursor, Size size, Rect bounds, double gap) {
        Point[] candidates = {
            new Point(cursor.X + gap, cursor.Y + gap),
            new Point(cursor.X - gap - size.Width, cursor.Y + gap),
            new Point(cursor.X + gap, cursor.Y - gap - size.Height),
            new Point(cursor.X - gap - size.Width, cursor.Y - gap - size.Height)
        };
        foreach(Point candidate in candidates) {
            if(fits(candidate, size, bounds)) return candidate;
        }
        // Passt nirgends ganz (sehr kleiner Monitor): erste Ecke in den Monitor schieben
        return ClampInto(candidates[0], size, bounds);
    }

    /// <summary>
    /// Verschiebt ein Rechteck der Größe <paramref name="size"/> so, dass es in <paramref name="bounds"/> liegt
    /// (ist es größer, bündig links/oben).
    /// </summary>
    public static Point ClampInto(Point topLeft, Size size, Rect bounds) {
        double x = Math.Max(bounds.Left, Math.Min(topLeft.X, bounds.Right - size.Width));
        double y = Math.Max(bounds.Top, Math.Min(topLeft.Y, bounds.Bottom - size.Height));
        return new Point(x, y);
    }

    /// <summary>Farbwert als <c>#RRGGBB</c>.</summary>
    public static string FormatColor(byte red, byte green, byte blue) {
        return string.Create(CultureInfo.InvariantCulture, $"#{red:X2}{green:X2}{blue:X2}");
    }

    /// <summary>
    /// Textzeile unter der Lupe: <c>X, Y · B × H · #RRGGBB</c> (ohne Bereich ohne den Größenteil).
    /// </summary>
    public static string FormatInfo(PixelPoint point, PixelRect? selection, string color) {
        string position = string.Create(CultureInfo.InvariantCulture, $"{point.X}, {point.Y}");
        if(!selection.HasValue) return $"{position} · {color}";
        string size = string.Create(CultureInfo.InvariantCulture, $"{selection.Value.Width} × {selection.Value.Height}");
        return $"{position} · {size} · {color}";
    }

    private static bool fits(Point topLeft, Size size, Rect bounds) {
        return topLeft.X >= bounds.Left && topLeft.Y >= bounds.Top && topLeft.X + size.Width <= bounds.Right && topLeft.Y + size.Height <= bounds.Bottom;
    }
}
