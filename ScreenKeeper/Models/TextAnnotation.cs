using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Text; <see cref="X"/>/<see cref="Y"/> ist die linke obere Ecke des Textfelds.
/// </summary>
public sealed class TextAnnotation : Annotation {
    [JsonIgnore]
    public override string Type => AnnotationTypes.Text;

    public int X { get; set; }

    public int Y { get; set; }

    public string Text { get; set; } = "";

    public string FontFamily { get; set; } = "Segoe UI";

    /// <summary>Schriftgröße in Bildpixeln.</summary>
    public double FontSize { get; set; } = 24;

    public bool Bold { get; set; }

    /// <summary>Textfarbe als #AARRGGBB.</summary>
    public string Foreground { get; set; } = "#FFE53935";

    /// <summary>Hintergrundfarbe als #AARRGGBB; null = kein Hintergrund.</summary>
    public string? Background { get; set; }

    /// <summary>Breite des Textfelds in Bildpixeln (Zeilenumbruch); null = ohne Umbruch, so breit wie der Text.</summary>
    public int? Width { get; set; }
}
