using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Verpixelter Bereich; wird bei Anzeige und Export aus dem Original berechnet (nicht destruktiv).
/// </summary>
public sealed class PixelateAnnotation : Annotation {
    [JsonIgnore]
    public override string Type => AnnotationTypes.Pixelate;

    public PixelRect Rect { get; set; }

    /// <summary>Kantenlänge eines Blocks in Bildpixeln.</summary>
    public int BlockSize { get; set; } = 10;
}
