using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Punkt in Bildpixeln. Wird in JSON kompakt als Array <c>[x, y]</c> geschrieben.
/// </summary>
[JsonConverter(typeof(PixelPointJsonConverter))]
public record struct PixelPoint(int X, int Y);

/// <summary>
/// Schreibt <see cref="PixelPoint"/> als <c>[x, y]</c>; liest zusätzlich die Objektform <c>{ "x": 1, "y": 2 }</c>.
/// </summary>
public sealed class PixelPointJsonConverter : JsonConverter<PixelPoint> {
    public override PixelPoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        if(reader.TokenType == JsonTokenType.StartArray) {
            reader.Read();
            int x = reader.GetInt32();
            reader.Read();
            int y = reader.GetInt32();
            reader.Read();
            // überzählige Werte tolerieren
            while(reader.TokenType != JsonTokenType.EndArray) {
                reader.Skip();
                reader.Read();
            }
            return new PixelPoint(x, y);
        }
        if(reader.TokenType == JsonTokenType.StartObject) {
            int x = 0;
            int y = 0;
            while(reader.Read() && reader.TokenType != JsonTokenType.EndObject) {
                string name = reader.GetString() ?? "";
                reader.Read();
                if(string.Equals(name, "x", StringComparison.OrdinalIgnoreCase)) {
                    x = reader.GetInt32();
                } else if(string.Equals(name, "y", StringComparison.OrdinalIgnoreCase)) {
                    y = reader.GetInt32();
                } else {
                    reader.Skip();
                }
            }
            return new PixelPoint(x, y);
        }
        throw new JsonException($"Ungültiger Punkt (Token {reader.TokenType}).");
    }

    public override void Write(Utf8JsonWriter writer, PixelPoint value, JsonSerializerOptions options) {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteEndArray();
    }
}
