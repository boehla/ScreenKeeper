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

    /// <summary>Farbe des Textmarkers (#AARRGGBB, deckend = volle Stärke; gezeichnet im Modus „Multiplizieren“).</summary>
    public string HighlightColor { get; set; } = "#FFFFEB3B";

    public double Thickness { get; set; } = 4;

    /// <summary>Füllfarbe für Rechteck und Ellipse (#AARRGGBB); leer = keine Füllung.</summary>
    public string FillColor { get; set; } = "";

    /// <summary>Text mit Hintergrund (Kontrastfarbe zur Textfarbe).</summary>
    public bool TextBackground { get; set; }

    public bool Smooth { get; set; } = true;

    /// <summary>Feste Schriftgröße (Bildpixel); auch für freien Text, wenn <see cref="TextAutoFontSize"/> an ist.</summary>
    public double FontSize { get; set; } = 24;

    /// <summary>Neue Textfelder mit automatischer Schriftgröße.</summary>
    public bool TextAutoFontSize { get; set; } = true;

    /// <summary>Ausrichtung neuer Texte (siehe <see cref="TextAlignments"/>).</summary>
    public string TextAlignment { get; set; } = TextAlignments.TopLeft;

    public int BlockSize { get; set; } = 10;

    public int BlurRadius { get; set; } = 8;

    public double StampSize { get; set; } = 32;

    public string CursorKind { get; set; } = CursorKinds.Arrow;

    /// <summary>Linienenden je Werkzeug (siehe <see cref="LineCaps"/>).</summary>
    public string LineStartCap { get; set; } = LineAnnotation.DefaultStartCap;

    public string LineEndCap { get; set; } = LineAnnotation.DefaultEndCap;

    public string ArrowStartCap { get; set; } = ArrowAnnotation.DefaultStartCap;

    public string ArrowEndCap { get; set; } = ArrowAnnotation.DefaultEndCap;

    public string FreehandStartCap { get; set; } = FreehandAnnotation.DefaultStartCap;

    public string FreehandEndCap { get; set; } = FreehandAnnotation.DefaultEndCap;

    /// <summary>Symbol am Anfang für neue Objekte des Typs <paramref name="annotationType"/> (Linie, Pfeil, Freihand).</summary>
    public string GetStartCap(string? annotationType) {
        switch(annotationType) {
            case AnnotationTypes.Line: return LineStartCap;
            case AnnotationTypes.Arrow: return ArrowStartCap;
            case AnnotationTypes.Freehand: return FreehandStartCap;
        }
        return LineCaps.None;
    }

    /// <summary>Symbol am Ende für neue Objekte des Typs <paramref name="annotationType"/>.</summary>
    public string GetEndCap(string? annotationType) {
        switch(annotationType) {
            case AnnotationTypes.Line: return LineEndCap;
            case AnnotationTypes.Arrow: return ArrowEndCap;
            case AnnotationTypes.Freehand: return FreehandEndCap;
        }
        return LineCaps.None;
    }

    public void SetStartCap(string? annotationType, string cap) {
        switch(annotationType) {
            case AnnotationTypes.Line: LineStartCap = cap; break;
            case AnnotationTypes.Arrow: ArrowStartCap = cap; break;
            case AnnotationTypes.Freehand: FreehandStartCap = cap; break;
        }
    }

    public void SetEndCap(string? annotationType, string cap) {
        switch(annotationType) {
            case AnnotationTypes.Line: LineEndCap = cap; break;
            case AnnotationTypes.Arrow: ArrowEndCap = cap; break;
            case AnnotationTypes.Freehand: FreehandEndCap = cap; break;
        }
    }

    public static ToolOptions FromSettings(AppSettings settings) {
        return new ToolOptions {
            Color = AnnotationColors.TryParse(settings.DefaultColor, out _) ? settings.DefaultColor : AnnotationColors.Red,
            HighlightColor = AnnotationColors.TryParse(settings.HighlightColor, out _) ? settings.HighlightColor : "#FFFFEB3B",
            Thickness = settings.DefaultThickness,
            FillColor = AnnotationColors.TryParse(settings.DefaultFillColor, out _) ? settings.DefaultFillColor : "",
            TextBackground = settings.DefaultTextBackground,
            Smooth = settings.DefaultSmooth,
            FontSize = settings.DefaultFontSize,
            TextAutoFontSize = settings.DefaultTextAutoFontSize,
            TextAlignment = TextAlignments.Normalize(settings.DefaultTextAlignment, TextAlignments.TopLeft),
            BlockSize = settings.DefaultBlockSize,
            BlurRadius = settings.DefaultBlurRadius,
            StampSize = settings.DefaultStampSize,
            CursorKind = settings.DefaultCursorKind,
            LineStartCap = LineCaps.Normalize(settings.DefaultLineStartCap, LineAnnotation.DefaultStartCap),
            LineEndCap = LineCaps.Normalize(settings.DefaultLineEndCap, LineAnnotation.DefaultEndCap),
            ArrowStartCap = LineCaps.Normalize(settings.DefaultArrowStartCap, ArrowAnnotation.DefaultStartCap),
            ArrowEndCap = LineCaps.Normalize(settings.DefaultArrowEndCap, ArrowAnnotation.DefaultEndCap),
            FreehandStartCap = LineCaps.Normalize(settings.DefaultFreehandStartCap, FreehandAnnotation.DefaultStartCap),
            FreehandEndCap = LineCaps.Normalize(settings.DefaultFreehandEndCap, FreehandAnnotation.DefaultEndCap)
        };
    }

    /// <summary>
    /// Werte in die Einstellungen übernehmen; true, wenn sich etwas geändert hat.
    /// </summary>
    public bool ApplyTo(AppSettings settings) {
        bool changed = settings.DefaultColor != Color
            || settings.HighlightColor != HighlightColor
            || settings.DefaultThickness != Thickness
            || settings.DefaultFillColor != FillColor
            || settings.DefaultTextBackground != TextBackground
            || settings.DefaultSmooth != Smooth
            || settings.DefaultFontSize != FontSize
            || settings.DefaultTextAutoFontSize != TextAutoFontSize
            || settings.DefaultTextAlignment != TextAlignment
            || settings.DefaultBlockSize != BlockSize
            || settings.DefaultBlurRadius != BlurRadius
            || settings.DefaultStampSize != StampSize
            || settings.DefaultCursorKind != CursorKind
            || settings.DefaultLineStartCap != LineStartCap
            || settings.DefaultLineEndCap != LineEndCap
            || settings.DefaultArrowStartCap != ArrowStartCap
            || settings.DefaultArrowEndCap != ArrowEndCap
            || settings.DefaultFreehandStartCap != FreehandStartCap
            || settings.DefaultFreehandEndCap != FreehandEndCap;
        if(!changed) return false;
        settings.DefaultColor = Color;
        settings.HighlightColor = HighlightColor;
        settings.DefaultThickness = Thickness;
        settings.DefaultFillColor = FillColor;
        settings.DefaultTextBackground = TextBackground;
        settings.DefaultSmooth = Smooth;
        settings.DefaultFontSize = FontSize;
        settings.DefaultTextAutoFontSize = TextAutoFontSize;
        settings.DefaultTextAlignment = TextAlignment;
        settings.DefaultBlockSize = BlockSize;
        settings.DefaultBlurRadius = BlurRadius;
        settings.DefaultStampSize = StampSize;
        settings.DefaultCursorKind = CursorKind;
        settings.DefaultLineStartCap = LineStartCap;
        settings.DefaultLineEndCap = LineEndCap;
        settings.DefaultArrowStartCap = ArrowStartCap;
        settings.DefaultArrowEndCap = ArrowEndCap;
        settings.DefaultFreehandStartCap = FreehandStartCap;
        settings.DefaultFreehandEndCap = FreehandEndCap;
        return true;
    }
}
