using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Pfeil von <c>Points[0]</c> (Anfang) nach <c>Points[1]</c> (Spitze).
/// </summary>
public sealed class ArrowAnnotation : Annotation {
    [JsonIgnore]
    public override string Type => AnnotationTypes.Arrow;

    public List<PixelPoint> Points { get; set; } = new List<PixelPoint>();

    /// <summary>Farbe als #AARRGGBB.</summary>
    public string Stroke { get; set; } = "#FFE53935";

    public double Thickness { get; set; } = 4;
}
