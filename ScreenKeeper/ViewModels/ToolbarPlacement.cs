using System.Windows;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// UI-freie Positionierung der schwebenden Werkzeugleiste (in Ansichts-DIPs).
/// </summary>
public static class ToolbarPlacement {
    /// <summary>
    /// Ohne Bereich oben mittig in <paramref name="bounds"/> (primärer Monitor). Mit Bereich mittig unter dem Bereich,
    /// bei Platzmangel darüber, sonst innerhalb am unteren Rand des Bereichs. Immer vollständig in <paramref name="bounds"/>.
    /// </summary>
    public static Point Place(Rect? selection, Size size, Rect bounds, double gap) {
        if(!selection.HasValue) {
            return MagnifierPlacement.ClampInto(new Point(bounds.Left + (bounds.Width - size.Width) / 2, bounds.Top + gap), size, bounds);
        }
        Rect area = selection.Value;
        double x = area.Left + (area.Width - size.Width) / 2;
        double below = area.Bottom + gap;
        double above = area.Top - gap - size.Height;
        double y;
        if(below + size.Height <= bounds.Bottom) {
            y = below;
        } else if(above >= bounds.Top) {
            y = above;
        } else {
            y = area.Bottom - gap - size.Height;
        }
        return MagnifierPlacement.ClampInto(new Point(x, y), size, bounds);
    }
}
