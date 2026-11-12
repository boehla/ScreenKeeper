using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Textmarker: Rechteck, das der Renderer im Modus „Multiplizieren“ auf den Untergrund legt (Text bleibt dunkel).
/// </summary>
public sealed class HighlightAnnotation : Annotation {
    [JsonIgnore]
    public override string Type => AnnotationTypes.Highlight;

    public PixelRect Rect { get; set; }

    /// <summary>Farbe als #AARRGGBB (Standard Gelb); der Alpha-Wert ist die Stärke des Markers.</summary>
    public string Color { get; set; } = "#FFFFEB3B";
}
