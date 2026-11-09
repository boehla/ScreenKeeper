using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Weichgezeichneter Bereich; wird bei Anzeige und Export aus dem Original berechnet (nicht destruktiv).
/// </summary>
public sealed class BlurAnnotation : Annotation {
    [JsonIgnore]
    public override string Type => AnnotationTypes.Blur;

    public PixelRect Rect { get; set; }

    /// <summary>Stärke (Radius in Bildpixeln).</summary>
    public int Radius { get; set; } = 8;
}
