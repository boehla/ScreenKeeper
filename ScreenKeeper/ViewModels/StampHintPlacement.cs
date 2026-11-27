using System.Windows;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Lage des Tastenhinweises („+/-: Nummer“) zur Stempel-Vorschau am Zeiger – UI-frei und damit testbar (ab 0.16).
/// </summary>
public static class StampHintPlacement {
    /// <summary>
    /// Linke obere Ecke des Hinweises (Ansichts-DIPs): mittig unter dem Stempel, mit <paramref name="gap"/> Abstand;
    /// passt er dort nicht mehr in <paramref name="bounds"/> (Monitor), über dem Stempel. Waagrecht bleibt er in den
    /// Grenzen.
    /// </summary>
    public static Point Place(Point center, double radius, Size hint, Rect bounds, double gap) {
        double x = center.X - hint.Width / 2;
        x = Math.Max(bounds.Left, Math.Min(x, bounds.Right - hint.Width));
        double y = center.Y + radius + gap;
        if(y + hint.Height > bounds.Bottom) y = center.Y - radius - gap - hint.Height;
        return new Point(x, y);
    }
}
