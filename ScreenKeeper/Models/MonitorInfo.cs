using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Ein Monitor zum Zeitpunkt der Aufnahme. <see cref="Bounds"/> in Bildpixeln.
/// </summary>
public sealed class MonitorInfo {
    /// <summary>Gerätename, z. B. <c>\\.\DISPLAY1</c>.</summary>
    public string Device { get; set; } = "";

    public PixelRect Bounds { get; set; }

    public bool Primary { get; set; }

    /// <summary>Skalierungsfaktor (1.0 = 100 %, 1.5 = 150 %).</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>Unbekannte Felder (z. B. aus neueren Versionen) bleiben beim Speichern erhalten.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
