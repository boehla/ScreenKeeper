using System.Windows.Media;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Welche Werkzeug-Eigenschaft eine Auswahl in einem Werkzeugleisten-Menü setzt.
/// </summary>
public enum OptionKind {
    Color,
    Thickness,
    FontSize,
    BlockSize,
    BlurRadius,
    StampSize,
    CursorKind,
    FillColor,
    StartCap,
    EndCap
}

/// <summary>
/// Ein Eintrag in einem Auswahlmenü der Werkzeugleiste (Farbe, Strichstärke, Schriftgröße …).
/// </summary>
public sealed class OptionChoice {
    public OptionChoice(OptionKind kind, object value, string label, Brush? brush = null, Geometry? icon = null) {
        Kind = kind;
        Value = value;
        Label = label;
        Brush = brush;
        Icon = icon;
    }

    public OptionKind Kind { get; }

    /// <summary>Wert: string (Farbe, Zeigerart, Linienende), double (Stärke, Schrift-, Stempelgröße) oder int (Block, Radius).</summary>
    public object Value { get; }

    public string Label { get; }

    /// <summary>Farbfeld (nur bei Farben).</summary>
    public Brush? Brush { get; }

    /// <summary>Symbol (nur bei Linienenden).</summary>
    public Geometry? Icon { get; }

    /// <summary>Strichstärke für die Vorschau-Linie im Menü (nur bei Strichstärken).</summary>
    public double PreviewThickness => Value is double thickness && Kind == OptionKind.Thickness ? Math.Min(thickness, 12) : 0;
}
