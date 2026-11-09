using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Der aufgenommene Mauszeiger. <see cref="X"/>/<see cref="Y"/> ist die Position des Hotspots in Bildpixeln;
/// das Bild wird bei (X - HotspotX, Y - HotspotY) gezeichnet.
/// </summary>
public sealed class CursorInfo {
    /// <summary>Zeigerbild als PNG (Base64, mit Transparenz). Null, wenn kein Zeiger ermittelt werden konnte.</summary>
    public string? Png { get; set; }

    public int X { get; set; }

    public int Y { get; set; }

    public int HotspotX { get; set; }

    public int HotspotY { get; set; }

    /// <summary>War der Zeiger bei der Aufnahme sichtbar?</summary>
    public bool CapturedVisible { get; set; }

    /// <summary>Unbekannte Felder (z. B. aus neueren Versionen) bleiben beim Speichern erhalten.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
