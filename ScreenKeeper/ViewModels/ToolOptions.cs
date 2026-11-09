using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Aktuelle Einstellungen der Zeichenwerkzeuge (zuletzt verwendete Werte). Werden aus den <see cref="AppSettings"/>
/// geladen und beim Schließen des Editors dorthin zurückgeschrieben.
/// </summary>
public sealed class ToolOptions {
    public static IReadOnlyList<double> Thicknesses { get; } = new double[] { 1, 2, 4, 6, 8, 12 };

    public static IReadOnlyList<double> FontSizes { get; } = new double[] { 12, 14, 16, 20, 24, 32, 40, 48, 64 };

    public static IReadOnlyList<int> BlockSizes { get; } = new[] { 4, 6, 8, 10, 14, 20, 30 };

    public static IReadOnlyList<int> BlurRadii { get; } = new[] { 2, 4, 6, 8, 12, 16, 24 };

    public static IReadOnlyList<double> StampSizes { get; } = new double[] { 20, 24, 32, 40, 48, 64 };

    /// <summary>Farbe für Rahmen, Linien, Text und Stempel (#AARRGGBB).</summary>
    public string Color { get; set; } = AnnotationColors.Red;

    /// <summary>Farbe des Textmarkers (#AARRGGBB, deckend; die Transparenz legt der Renderer fest).</summary>
    public string HighlightColor { get; set; } = "#FFFFEB3B";

    public double Thickness { get; set; } = 4;

    /// <summary>Rechteck und Ellipse in der Rahmenfarbe füllen.</summary>
    public bool Fill { get; set; }

    /// <summary>Text mit Hintergrund (Kontrastfarbe zur Textfarbe).</summary>
    public bool TextBackground { get; set; }

    public bool Smooth { get; set; } = true;

    public double FontSize { get; set; } = 24;

    public int BlockSize { get; set; } = 10;

    public int BlurRadius { get; set; } = 8;

    public double StampSize { get; set; } = 32;

    public string CursorKind { get; set; } = CursorKinds.Arrow;

    public static ToolOptions FromSettings(AppSettings settings) {
        return new ToolOptions {
            Color = AnnotationColors.TryParse(settings.DefaultColor, out _) ? settings.DefaultColor : AnnotationColors.Red,
            HighlightColor = AnnotationColors.TryParse(settings.HighlightColor, out _) ? settings.HighlightColor : "#FFFFEB3B",
            Thickness = settings.DefaultThickness,
            Fill = settings.DefaultFill,
            TextBackground = settings.DefaultTextBackground,
            Smooth = settings.DefaultSmooth,
            FontSize = settings.DefaultFontSize,
            BlockSize = settings.DefaultBlockSize,
            BlurRadius = settings.DefaultBlurRadius,
            StampSize = settings.DefaultStampSize,
            CursorKind = settings.DefaultCursorKind
        };
    }

    /// <summary>
    /// Werte in die Einstellungen übernehmen; true, wenn sich etwas geändert hat.
    /// </summary>
    public bool ApplyTo(AppSettings settings) {
        bool changed = settings.DefaultColor != Color
            || settings.HighlightColor != HighlightColor
            || settings.DefaultThickness != Thickness
            || settings.DefaultFill != Fill
            || settings.DefaultTextBackground != TextBackground
            || settings.DefaultSmooth != Smooth
            || settings.DefaultFontSize != FontSize
            || settings.DefaultBlockSize != BlockSize
            || settings.DefaultBlurRadius != BlurRadius
            || settings.DefaultStampSize != StampSize
            || settings.DefaultCursorKind != CursorKind;
        if(!changed) return false;
        settings.DefaultColor = Color;
        settings.HighlightColor = HighlightColor;
        settings.DefaultThickness = Thickness;
        settings.DefaultFill = Fill;
        settings.DefaultTextBackground = TextBackground;
        settings.DefaultSmooth = Smooth;
        settings.DefaultFontSize = FontSize;
        settings.DefaultBlockSize = BlockSize;
        settings.DefaultBlurRadius = BlurRadius;
        settings.DefaultStampSize = StampSize;
        settings.DefaultCursorKind = CursorKind;
        return true;
    }
}
