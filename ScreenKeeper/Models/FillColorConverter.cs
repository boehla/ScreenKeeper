using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Liest die Füllung von Rechteck und Ellipse tolerant: eine Farbe (<c>#AARRGGBB</c>) bleibt eine Farbe, <c>false</c> und
/// <c>null</c> bedeuten keine Füllung, <c>true</c> (ältere bzw. handgeschriebene Dateien) bedeutet „in der Rahmenfarbe
/// füllen“ – das Objekt ersetzt dafür <see cref="StrokeMarker"/> nach dem Lesen durch seine Rahmenfarbe.
/// Geschrieben wird immer die Farbe.
/// </summary>
public sealed class FillColorConverter : JsonConverter<string?> {
    /// <summary>Platzhalter für <c>"fill": true</c> bis zum Ende des Lesens (keine gültige Farbe).</summary>
    public const string StrokeMarker = "stroke";

    public override bool HandleNull => true;

    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        switch(reader.TokenType) {
            case JsonTokenType.String: return reader.GetString();
            case JsonTokenType.True: return StrokeMarker;
            case JsonTokenType.False:
            case JsonTokenType.Null:
                return null;
        }
        throw new JsonException($"\"fill\" muss eine Farbe, true/false oder null sein (gefunden: {reader.TokenType}).");
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) {
        if(value == null) {
            writer.WriteNullValue();
        } else {
            writer.WriteStringValue(value);
        }
    }

    /// <summary>Füllwert nach dem Lesen: <see cref="StrokeMarker"/> wird zur Rahmenfarbe, leer zu null.</summary>
    public static string? Resolve(string? fill, string stroke) {
        if(string.IsNullOrEmpty(fill)) return null;
        return fill == StrokeMarker ? stroke : fill;
    }
}
