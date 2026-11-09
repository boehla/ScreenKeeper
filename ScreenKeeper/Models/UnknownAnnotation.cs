using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Platzhalter für einen beim Lesen unbekannten Annotation-Typ (z. B. aus einer neueren Programmversion).
/// Die Rohdaten werden beim Speichern unverändert zurückgeschrieben; gezeichnet wird nichts.
/// </summary>
public sealed class UnknownAnnotation : Annotation {
    public UnknownAnnotation(JsonElement raw) {
        Raw = raw.Clone();
        if(Raw.ValueKind == JsonValueKind.Object && Raw.TryGetProperty("id", out JsonElement id) && id.ValueKind == JsonValueKind.String) {
            Id = id.GetString() ?? "";
        } else {
            Id = "";
        }
    }

    /// <summary>Das vollständige JSON-Objekt, wie es gelesen wurde.</summary>
    [JsonIgnore]
    public JsonElement Raw { get; }

    [JsonIgnore]
    public override string Type {
        get {
            if(Raw.ValueKind == JsonValueKind.Object && Raw.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String) {
                return type.GetString() ?? "";
            }
            return "";
        }
    }
}
