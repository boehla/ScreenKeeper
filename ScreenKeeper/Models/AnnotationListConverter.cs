using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Liest/schreibt die Annotation-Liste. Bekannte Typen laufen über die polymorphe Serialisierung von
/// <see cref="Annotation"/>; unbekannte Typen werden vorher per <see cref="JsonDocument"/> herausgefiltert,
/// als <see cref="UnknownAnnotation"/> aufgehoben und beim Schreiben unverändert zurückgeschrieben.
/// </summary>
public sealed class AnnotationListConverter : JsonConverter<List<Annotation>> {
    private static readonly HashSet<string> knownTypes = loadKnownTypes();

    /// <summary>Alle Diskriminatoren, die über <see cref="JsonDerivedTypeAttribute"/> registriert sind.</summary>
    public static IReadOnlyCollection<string> KnownTypes => knownTypes;

    public override List<Annotation> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
        List<Annotation> result = new List<Annotation>();
        using (JsonDocument document = JsonDocument.ParseValue(ref reader)) {
            JsonElement root = document.RootElement;
            if(root.ValueKind != JsonValueKind.Array) throw new JsonException("\"annotations\" muss ein Array sein.");
            foreach(JsonElement element in root.EnumerateArray()) {
                if(isKnown(element)) {
                    Annotation? annotation = element.Deserialize<Annotation>(options);
                    if(annotation != null) result.Add(annotation);
                } else {
                    result.Add(new UnknownAnnotation(element));
                }
            }
        }
        return result;
    }

    public override void Write(Utf8JsonWriter writer, List<Annotation> value, JsonSerializerOptions options) {
        writer.WriteStartArray();
        foreach(Annotation annotation in value) {
            if(annotation is UnknownAnnotation unknown) {
                unknown.Raw.WriteTo(writer);
            } else {
                JsonSerializer.Serialize(writer, annotation, typeof(Annotation), options);
            }
        }
        writer.WriteEndArray();
    }

    private static bool isKnown(JsonElement element) {
        if(element.ValueKind != JsonValueKind.Object) return false;
        if(!element.TryGetProperty("type", out JsonElement type) || type.ValueKind != JsonValueKind.String) return false;
        return knownTypes.Contains(type.GetString() ?? "");
    }

    private static HashSet<string> loadKnownTypes() {
        HashSet<string> types = new HashSet<string>(StringComparer.Ordinal);
        foreach(JsonDerivedTypeAttribute attribute in typeof(Annotation).GetCustomAttributes<JsonDerivedTypeAttribute>(false)) {
            if(attribute.TypeDiscriminator is string name) types.Add(name);
        }
        return types;
    }
}
