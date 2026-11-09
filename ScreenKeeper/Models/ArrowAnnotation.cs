using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Pfeil von <c>Points[0]</c> (Anfang) nach <c>Points[1]</c> (Spitze). Die Enden sind wählbar; der Standard
/// (ohne Symbol am Anfang, Pfeilspitze am Ende) entspricht Dateien ohne <c>startCap</c>/<c>endCap</c>.
/// </summary>
public sealed class ArrowAnnotation : Annotation, ILineCapAnnotation {
    public const string DefaultStartCap = LineCaps.None;
    public const string DefaultEndCap = LineCaps.Arrow;

    private string startCap = DefaultStartCap;
    private string endCap = DefaultEndCap;

    [JsonIgnore]
    public override string Type => AnnotationTypes.Arrow;

    public List<PixelPoint> Points { get; set; } = new List<PixelPoint>();

    /// <summary>Farbe als #AARRGGBB.</summary>
    public string Stroke { get; set; } = "#FFE53935";

    public double Thickness { get; set; } = 4;

    /// <summary>Symbol am Anfang (siehe <see cref="LineCaps"/>); fehlt das Feld, gilt <see cref="DefaultStartCap"/>.</summary>
    public string StartCap {
        get => startCap;
        set => startCap = value ?? DefaultStartCap;
    }

    /// <summary>Symbol an der Spitze (siehe <see cref="LineCaps"/>); fehlt das Feld, gilt <see cref="DefaultEndCap"/>.</summary>
    public string EndCap {
        get => endCap;
        set => endCap = value ?? DefaultEndCap;
    }
}
