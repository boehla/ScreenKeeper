using System.Windows;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// UI-freie Positionierung der schwebenden Werkzeugleiste (in Ansichts-DIPs).
/// </summary>
public static class ToolbarPlacement {
    /// <summary>
    /// Ohne Bereich oben mittig in <paramref name="bounds"/> (primärer Monitor), mit <paramref name="flipped"/> unten mittig
    /// (ausgewichen, siehe <see cref="UpdateFlipped"/>). Mit Bereich mittig unter dem Bereich, bei Platzmangel darüber,
    /// sonst innerhalb am unteren Rand des Bereichs (<paramref name="flipped"/> spielt dann keine Rolle). Immer vollständig
    /// in <paramref name="bounds"/>.
    /// </summary>
    public static Point Place(Rect? selection, Size size, Rect bounds, double gap, bool flipped = false) {
        if(!selection.HasValue) {
            double top = flipped ? bounds.Bottom - gap - size.Height : bounds.Top + gap;
            return MagnifierPlacement.ClampInto(new Point(bounds.Left + (bounds.Width - size.Width) / 2, top), size, bounds);
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

    /// <summary>
    /// Ausweichen ohne Bereich: Kommt der Zeiger der Leiste auf <paramref name="margin"/> nahe (oder ist er schon darüber)
    /// und darf sie ausweichen (<paramref name="canEvade"/>: Werkzeug „Bereich“, Leiste wird nicht bedient, Strg nicht
    /// gedrückt), wechselt sie an die gegenüberliegende Kante (oben mittig ↔ unten mittig) – außer der Zeiger wäre dort
    /// ebenso nahe, dann bleibt sie. Mit einem Bereich gilt wieder die normale Platzierung (Ergebnis false).
    /// </summary>
    /// <returns>Ob die Leiste unten steht (für <see cref="Place"/>).</returns>
    public static bool UpdateFlipped(bool flipped, bool canEvade, Rect? selection, Point pointer, Size size, Rect bounds, double gap, double margin) {
        if(selection.HasValue) return false;
        if(!canEvade) return flipped;
        if(!isNear(Place(null, size, bounds, gap, flipped), size, pointer, margin)) return flipped;
        return isNear(Place(null, size, bounds, gap, !flipped), size, pointer, margin) ? flipped : !flipped;
    }

    private static bool isNear(Point topLeft, Size size, Point pointer, double margin) {
        Rect zone = new Rect(topLeft, size);
        zone.Inflate(margin, margin);
        return zone.Contains(pointer);
    }
}
