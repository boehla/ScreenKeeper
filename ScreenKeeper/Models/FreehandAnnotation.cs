using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Freihand-Linie durch alle <see cref="Points"/>, optional mit Symbolen an den Enden.
/// </summary>
public sealed class FreehandAnnotation : Annotation, ILineCapAnnotation {
    public const string DefaultStartCap = LineCaps.None;
    public const string DefaultEndCap = LineCaps.None;

    private string startCap = DefaultStartCap;
    private string endCap = DefaultEndCap;

    [JsonIgnore]
    public override string Type => AnnotationTypes.Freehand;

    public List<PixelPoint> Points { get; set; } = new List<PixelPoint>();

    /// <summary>Farbe als #AARRGGBB.</summary>
    public string Stroke { get; set; } = "#FFE53935";

    public double Thickness { get; set; } = 4;

    /// <summary>Linie beim Zeichnen glätten.</summary>
    public bool Smooth { get; set; } = true;

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
