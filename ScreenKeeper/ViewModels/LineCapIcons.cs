using System.Windows;
using System.Windows.Media;
using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Symbole (20 × 20, für Strich und Füllung in Weiß) und Anzeigetexte der Linienenden für die Werkzeugleiste, sowie das
/// Farbfeld für „keine Füllung“.
/// </summary>
public static class LineCapIcons {
    private static readonly Dictionary<(string Cap, bool AtStart), Geometry> cache = new Dictionary<(string Cap, bool AtStart), Geometry>();
    private static readonly object sync = new object();
    private static Brush? noFillBrush;

    /// <summary>Symbol für das Ende (Symbol rechts) bzw. den Anfang (<paramref name="atStart"/>, gespiegelt, Symbol links).</summary>
    public static Geometry Get(string cap, bool atStart) {
        lock(sync) {
            if(cache.TryGetValue((cap, atStart), out Geometry? cached)) return cached;
            Geometry geometry = Geometry.Parse(getPathData(cap));
            if(atStart) geometry.Transform = new ScaleTransform(-1, 1, 10, 10);
            geometry.Freeze();
            cache[(cap, atStart)] = geometry;
            return geometry;
        }
    }

    public static string GetLabel(string cap) {
        switch(cap) {
            case LineCaps.Arrow: return Strings.LineCapArrow;
            case LineCaps.Dot: return Strings.LineCapDot;
            case LineCaps.Square: return Strings.LineCapSquare;
            case LineCaps.Triangle: return Strings.LineCapTriangle;
        }
        return Strings.LineCapNone;
    }

    /// <summary>Farbfeld für eine Füllfarbe; leer = „keine Füllung“ (rot durchgestrichen).</summary>
    public static Brush GetFillBrush(string? fill) {
        if(!string.IsNullOrEmpty(fill)) return AnnotationColors.GetBrush(fill);
        lock(sync) {
            return noFillBrush ??= createNoFillBrush();
        }
    }

    private static string getPathData(string cap) {
        switch(cap) {
            case LineCaps.Arrow: return "M2,10 H12 M18,10 L11,6 L11,14 Z";
            case LineCaps.Dot: return "M2,10 H14 M18,10 A3.5,3.5 0 1 1 11,10 A3.5,3.5 0 1 1 18,10 Z";
            case LineCaps.Square: return "M2,10 H14 M11,6.5 H18 V13.5 H11 Z";
            case LineCaps.Triangle: return "M2,10 H14 M18.5,10 L11.75,6.1 L11.75,13.9 Z";
        }
        return "M2,10 H18";
    }

    private static Brush createNoFillBrush() {
        DrawingGroup group = new DrawingGroup();
        using (DrawingContext dc = group.Open()) {
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, 20, 20));
            Pen pen = new Pen(AnnotationColors.GetBrush(AnnotationColors.Red), 2);
            pen.Freeze();
            dc.DrawLine(pen, new Point(1, 19), new Point(19, 1));
        }
        DrawingBrush brush = new DrawingBrush(group) { Stretch = Stretch.Fill };
        brush.Freeze();
        return brush;
    }
}
