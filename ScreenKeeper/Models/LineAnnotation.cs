using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Gerade Linie von <c>Points[0]</c> nach <c>Points[1]</c>, optional mit Symbolen an den Enden.
/// </summary>
public sealed class LineAnnotation : Annotation, ILineCapAnnotation {
    public const string DefaultStartCap = LineCaps.None;
    public const string DefaultEndCap = LineCaps.None;

    private string startCap = DefaultStartCap;
    private string endCap = DefaultEndCap;

    [JsonIgnore]
    public override string Type => AnnotationTypes.Line;

    public List<PixelPoint> Points { get; set; } = new List<PixelPoint>();

    /// <summary>Farbe als #AARRGGBB.</summary>
    public string Stroke { get; set; } = "#FFE53935";

    public double Thickness { get; set; } = 4;

    /// <summary>Symbol am Anfang (siehe <see cref="LineCaps"/>); fehlt das Feld, gilt <see cref="DefaultStartCap"/>.</summary>
    public string StartCap {
        get => startCap;
        set => startCap = value ?? DefaultStartCap;
    }

    /// <summary>Symbol am Ende (siehe <see cref="LineCaps"/>); fehlt das Feld, gilt <see cref="DefaultEndCap"/>.</summary>
    public string EndCap {
        get => endCap;
        set => endCap = value ?? DefaultEndCap;
    }
}
