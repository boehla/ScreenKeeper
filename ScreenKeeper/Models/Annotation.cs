using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Basisklasse aller Zeichenobjekte. Alle Koordinaten in Bildpixeln des Original-PNGs.
/// Der JSON-Diskriminator heißt <c>type</c> (siehe <see cref="AnnotationTypes"/>).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(RectangleAnnotation), AnnotationTypes.Rectangle)]
[JsonDerivedType(typeof(EllipseAnnotation), AnnotationTypes.Ellipse)]
[JsonDerivedType(typeof(LineAnnotation), AnnotationTypes.Line)]
[JsonDerivedType(typeof(ArrowAnnotation), AnnotationTypes.Arrow)]
[JsonDerivedType(typeof(FreehandAnnotation), AnnotationTypes.Freehand)]
[JsonDerivedType(typeof(TextAnnotation), AnnotationTypes.Text)]
[JsonDerivedType(typeof(HighlightAnnotation), AnnotationTypes.Highlight)]
[JsonDerivedType(typeof(PixelateAnnotation), AnnotationTypes.Pixelate)]
[JsonDerivedType(typeof(BlurAnnotation), AnnotationTypes.Blur)]
[JsonDerivedType(typeof(NumberStampAnnotation), AnnotationTypes.NumberStamp)]
[JsonDerivedType(typeof(CursorAnnotation), AnnotationTypes.Cursor)]
public abstract class Annotation {
    /// <summary>Eindeutige Kennung (Guid im Format "N").</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Typ-Diskriminator (wird von System.Text.Json selbst geschrieben, daher hier ignoriert).</summary>
    [JsonIgnore]
    public abstract string Type { get; }

    /// <summary>Unbekannte Felder (z. B. aus neueren Versionen) bleiben beim Speichern erhalten.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>
    /// Tiefe Kopie mit derselben <see cref="Id"/> (JSON-Rundreise mit den Optionen des Dokuments).
    /// </summary>
    public Annotation Clone() {
        if(this is UnknownAnnotation unknown) return new UnknownAnnotation(unknown.Raw);
        string json = JsonSerializer.Serialize(this, typeof(Annotation), CaptureDocumentSerializer.Options);
        Annotation? copy = JsonSerializer.Deserialize<Annotation>(json, CaptureDocumentSerializer.Options);
        if(copy == null) throw new InvalidOperationException($"Annotation {Id} konnte nicht kopiert werden.");
        return copy;
    }

    /// <summary>
    /// JSON-Darstellung (für Vergleiche „hat sich etwas geändert?“).
    /// </summary>
    public string ToJson() {
        if(this is UnknownAnnotation unknown) return unknown.Raw.GetRawText();
        return JsonSerializer.Serialize(this, typeof(Annotation), CaptureDocumentSerializer.Options);
    }
}

/// <summary>
/// Die Werte des JSON-Diskriminators <c>type</c>.
/// </summary>
public static class AnnotationTypes {
    public const string Rectangle = "rectangle";
    public const string Ellipse = "ellipse";
    public const string Line = "line";
    public const string Arrow = "arrow";
    public const string Freehand = "freehand";
    public const string Text = "text";
    public const string Highlight = "highlight";
    public const string Pixelate = "pixelate";
    public const string Blur = "blur";
    public const string NumberStamp = "number";
    public const string Cursor = "cursor";
}
