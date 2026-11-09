using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Das Programm, das bei der Aufnahme im Vordergrund war (aktives Fenster).
/// </summary>
public sealed class ForegroundInfo {
    /// <summary>Fenstertitel (kann leer sein).</summary>
    public string Title { get; set; } = "";

    /// <summary>Prozessname ohne Endung, z. B. <c>chrome</c>.</summary>
    public string Process { get; set; } = "";

    /// <summary>Unbekannte Felder (z. B. aus neueren Versionen) bleiben beim Speichern erhalten.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
