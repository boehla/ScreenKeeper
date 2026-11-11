using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScreenKeeper.Models;

/// <summary>
/// Metadaten und Bearbeitungsstand einer Aufnahme (die JSON-Datei neben dem PNG).
/// Alle Koordinaten in Bildpixeln des Original-PNGs; nur <see cref="VirtualScreen"/> hält Bildschirmkoordinaten fest.
/// </summary>
public sealed class CaptureDocument {
    public const int CurrentFormatVersion = 1;

    private string? comment;

    public int FormatVersion { get; set; } = CurrentFormatVersion;

    public DateTimeOffset CapturedAt { get; set; }

    /// <summary>Dateiname des Original-PNGs (ohne Ordner, relativ zur JSON-Datei).</summary>
    public string Image { get; set; } = "";

    /// <summary>Lage und Größe des virtuellen Bildschirms in Bildschirmkoordinaten (physische Pixel).</summary>
    public PixelRect VirtualScreen { get; set; }

    public List<MonitorInfo> Monitors { get; set; } = new List<MonitorInfo>();

    public List<WindowInfo> Windows { get; set; } = new List<WindowInfo>();

    /// <summary>Programm im Vordergrund bei der Aufnahme; null = unbekannt (z. B. ältere Dateien).</summary>
    public ForegroundInfo? Foreground { get; set; }

    public CursorInfo? Cursor { get; set; }

    /// <summary>
    /// Permanent markiert: wird beim automatischen Aufräumen nie gelöscht. Geschrieben nur, wenn true
    /// (fehlendes Feld = false).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Permanent { get; set; }

    /// <summary>
    /// Freier, mehrzeiliger Kommentar zur Aufnahme (durchsuchbar in der History); null = kein Kommentar. Leere bzw. nur aus
    /// Leerraum bestehende Werte werden beim Lesen und Setzen zu null und daher nicht geschrieben (Dateien ohne das Feld
    /// bleiben ohne es).
    /// </summary>
    public string? Comment {
        get => comment;
        set => comment = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>Gewählter Bereich; null = noch kein Bereich gewählt.</summary>
    public PixelRect? Selection { get; set; }

    /// <summary>Zeichenobjekte in Zeichenreihenfolge.</summary>
    [JsonConverter(typeof(AnnotationListConverter))]
    public List<Annotation> Annotations { get; set; } = new List<Annotation>();

    /// <summary>Erkannter Text (spätere Ausbaustufe); wird wie in der Spezifikation immer geschrieben, auch als null.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string? OcrText { get; set; }

    /// <summary>Unbekannte Felder (z. B. aus neueren Versionen) bleiben beim Speichern erhalten.</summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    [JsonIgnore]
    public bool HasComment => !string.IsNullOrEmpty(Comment);

    /// <summary>
    /// Einheitliche Form eines Kommentars: Leerraum an Anfang und Ende entfernt, Zeilenenden als CRLF; leer → null.
    /// </summary>
    public static string? NormalizeComment(string? comment) {
        if(string.IsNullOrWhiteSpace(comment)) return null;
        return comment.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n").Trim();
    }

    /// <summary>
    /// Hat die Aufnahme einen Bereich oder Zeichnungen (für das Kennzeichen „bearbeitet“)?
    /// Der automatisch angelegte, aufgenommene Mauszeiger zählt nicht als Bearbeitung.
    /// </summary>
    [JsonIgnore]
    public bool IsEdited {
        get {
            if(Selection.HasValue) return true;
            foreach(Annotation annotation in Annotations) {
                if(annotation is CursorAnnotation cursor && cursor.Kind == CursorKinds.Captured) continue;
                return true;
            }
            return false;
        }
    }
}
