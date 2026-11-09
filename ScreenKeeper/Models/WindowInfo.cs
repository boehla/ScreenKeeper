using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Sichtbares Top-Level-Fenster zum Zeitpunkt der Aufnahme (für späteres Einrasten).
/// </summary>
public sealed class WindowInfo {
    public string Title { get; set; } = "";

    /// <summary>Prozessname ohne Endung, z. B. <c>CalculatorApp</c>.</summary>
    public string Process { get; set; } = "";

    /// <summary>Sichtbarer Fensterrahmen (DWM) in Bildpixeln.</summary>
    public PixelRect Bounds { get; set; }

    /// <summary>Z-Reihenfolge: 0 = oberstes Fenster.</summary>
    public int Z { get; set; }

    /// <summary>Unbekannte Felder (z. B. aus neueren Versionen) bleiben beim Speichern erhalten.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
