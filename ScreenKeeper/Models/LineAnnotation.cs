using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Gerade Linie von <c>Points[0]</c> nach <c>Points[1]</c>.
/// </summary>
public sealed class LineAnnotation : Annotation {
    [JsonIgnore]
    public override string Type => AnnotationTypes.Line;

    public List<PixelPoint> Points { get; set; } = new List<PixelPoint>();

    /// <summary>Farbe als #AARRGGBB.</summary>
    public string Stroke { get; set; } = "#FFE53935";

    public double Thickness { get; set; } = 4;
}
