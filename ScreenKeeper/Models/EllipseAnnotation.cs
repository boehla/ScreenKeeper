using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Ellipse innerhalb von <see cref="Rect"/> mit Rahmen und optionaler Füllung.
/// </summary>
public sealed class EllipseAnnotation : Annotation {
    [JsonIgnore]
    public override string Type => AnnotationTypes.Ellipse;

    public PixelRect Rect { get; set; }

    /// <summary>Rahmenfarbe als #AARRGGBB.</summary>
    public string Stroke { get; set; } = "#FFE53935";

    public double Thickness { get; set; } = 4;

    /// <summary>Füllfarbe als #AARRGGBB; null = keine Füllung.</summary>
    public string? Fill { get; set; }
}
