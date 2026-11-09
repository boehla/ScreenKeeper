using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Nummern-Stempel (Kreis mit Zahl); <see cref="X"/>/<see cref="Y"/> ist der Mittelpunkt.
/// </summary>
public sealed class NumberStampAnnotation : Annotation {
    [JsonIgnore]
    public override string Type => AnnotationTypes.NumberStamp;

    public int X { get; set; }

    public int Y { get; set; }

    public int Number { get; set; } = 1;

    /// <summary>Kreisfarbe als #AARRGGBB.</summary>
    public string Color { get; set; } = "#FFE53935";

    /// <summary>Durchmesser in Bildpixeln.</summary>
    public double Size { get; set; } = 32;
}
