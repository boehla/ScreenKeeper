using System.Globalization;
using System.Windows.Media;
using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Symbole (20 × 20, Strich in Weiß) und Anzeigetexte der Textausrichtung für die Werkzeugleiste: ein Feld mit zwei
/// Textzeilen an der jeweiligen Stelle.
/// </summary>
public static class TextAlignmentIcons {
    private static readonly Dictionary<string, Geometry> cache = new Dictionary<string, Geometry>();
    private static readonly object sync = new object();

    public static Geometry Get(string? alignment) {
        string key = TextAlignments.Normalize(alignment, TextAlignments.TopLeft);
        lock(sync) {
            if(cache.TryGetValue(key, out Geometry? cached)) return cached;
            Geometry geometry = Geometry.Parse(getPathData(key));
            if(geometry.CanFreeze) geometry.Freeze();
            cache[key] = geometry;
            return geometry;
        }
    }

    public static string GetLabel(string? alignment) {
        switch(alignment) {
            case TextAlignments.TopCenter: return Strings.TextAlignTopCenter;
            case TextAlignments.TopRight: return Strings.TextAlignTopRight;
            case TextAlignments.MiddleLeft: return Strings.TextAlignMiddleLeft;
            case TextAlignments.MiddleCenter: return Strings.TextAlignMiddleCenter;
            case TextAlignments.MiddleRight: return Strings.TextAlignMiddleRight;
            case TextAlignments.BottomLeft: return Strings.TextAlignBottomLeft;
            case TextAlignments.BottomCenter: return Strings.TextAlignBottomCenter;
            case TextAlignments.BottomRight: return Strings.TextAlignBottomRight;
        }
        return Strings.TextAlignTopLeft;
    }

    /// <summary>Rahmen 2…18, darin eine lange (9) und eine kurze (5) Zeile, 3 Einheiten Abstand.</summary>
    private static string getPathData(string alignment) {
        int column = TextAlignments.GetColumn(alignment);
        int row = TextAlignments.GetRow(alignment);
        double firstY = row == 0 ? 6 : row == 1 ? 8.5 : 11;
        double secondY = firstY + 3;
        return "M2,2 H18 V18 H2 Z " + line(column, firstY, 9) + " " + line(column, secondY, 5);
    }

    private static string line(int column, double y, double length) {
        double left = column == 0 ? 5 : column == 1 ? 10 - length / 2 : 15 - length;
        return string.Format(CultureInfo.InvariantCulture, "M{0},{1} H{2}", left, y, left + length);
    }
}
