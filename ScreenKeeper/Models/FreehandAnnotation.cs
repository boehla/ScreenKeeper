using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Freihand-Linie durch alle <see cref="Points"/>.
/// </summary>
public sealed class FreehandAnnotation : Annotation {
    [JsonIgnore]
    public override string Type => AnnotationTypes.Freehand;

    public List<PixelPoint> Points { get; set; } = new List<PixelPoint>();

    /// <summary>Farbe als #AARRGGBB.</summary>
    public string Stroke { get; set; } = "#FFE53935";

    public double Thickness { get; set; } = 4;

    /// <summary>Linie beim Zeichnen glätten.</summary>
    public bool Smooth { get; set; } = true;
}
