using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Virtuelle Maus. <see cref="X"/>/<see cref="Y"/> ist die Hotspot-Position in Bildpixeln.
/// </summary>
public sealed class CursorAnnotation : Annotation {
    [JsonIgnore]
    public override string Type => AnnotationTypes.Cursor;

    public int X { get; set; }

    public int Y { get; set; }

    /// <summary>Zeigerart, siehe <see cref="CursorKinds"/>.</summary>
    public string Kind { get; set; } = CursorKinds.Captured;

    /// <summary>Ausgeblendete Zeiger bleiben gespeichert, werden aber nicht gezeichnet.</summary>
    public bool Visible { get; set; } = true;
}

/// <summary>
/// Werte für <see cref="CursorAnnotation.Kind"/>.
/// </summary>
public static class CursorKinds {
    /// <summary>Der bei der Aufnahme erfasste Zeiger (Bild aus <see cref="CaptureDocument.Cursor"/>).</summary>
    public const string Captured = "captured";
    public const string Arrow = "arrow";
    public const string Hand = "hand";
    public const string IBeam = "ibeam";
}
