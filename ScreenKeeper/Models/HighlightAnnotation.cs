using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Textmarker: halbtransparentes Rechteck. Die Transparenz legt der Renderer fest.
/// </summary>
public sealed class HighlightAnnotation : Annotation {
    [JsonIgnore]
    public override string Type => AnnotationTypes.Highlight;

    public PixelRect Rect { get; set; }

    /// <summary>Farbe als #AARRGGBB (Standard Gelb).</summary>
    public string Color { get; set; } = "#FFFFEB3B";
}
