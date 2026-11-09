using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Ellipse innerhalb von <see cref="Rect"/> mit Rahmen und optionaler Füllung in eigener Farbe.
/// </summary>
public sealed class EllipseAnnotation : Annotation, IJsonOnDeserialized {
    [JsonIgnore]
    public override string Type => AnnotationTypes.Ellipse;

    public PixelRect Rect { get; set; }

    /// <summary>Rahmenfarbe als #AARRGGBB.</summary>
    public string Stroke { get; set; } = "#FFE53935";

    public double Thickness { get; set; } = 4;

    /// <summary>
    /// Füllfarbe als #AARRGGBB (Transparenz erlaubt); null = keine Füllung. Ältere Dateien mit <c>"fill": true</c>
    /// werden in der Rahmenfarbe gefüllt (siehe <see cref="FillColorConverter"/>).
    /// </summary>
    [JsonConverter(typeof(FillColorConverter))]
    public string? Fill { get; set; }

    void IJsonOnDeserialized.OnDeserialized() {
        Fill = FillColorConverter.Resolve(Fill, Stroke);
    }
}
