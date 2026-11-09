using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenKeeper.Services;

namespace ScreenKeeper.Models;

/// <summary>
/// JSON-Lesen und -Schreiben von <see cref="CaptureDocument"/>. Schreiben erfolgt atomar (temporäre Datei + Umbenennen).
/// </summary>
public static class CaptureDocumentSerializer {
    public static JsonSerializerOptions Options { get; } = createOptions();

    public static string Serialize(CaptureDocument document) {
        return JsonSerializer.Serialize(document, Options);
    }

    public static CaptureDocument Deserialize(string json) {
        CaptureDocument? document = JsonSerializer.Deserialize<CaptureDocument>(json, Options);
        if(document == null) throw new InvalidDataException("Die JSON-Datei enthält kein Dokument.");
        if(document.FormatVersion > CaptureDocument.CurrentFormatVersion) {
            // Neuere Version: trotzdem lesen; Unbekanntes bleibt erhalten, die Versionsnummer wird nicht herabgesetzt
            Log.Warn($"Dokument hat formatVersion {document.FormatVersion}, unterstützt wird {CaptureDocument.CurrentFormatVersion} – unbekannte Inhalte bleiben unverändert");
        }
        normalize(document);
        return document;
    }

    public static void Save(CaptureDocument document, string path) {
        AtomicFile.WriteAllText(path, Serialize(document));
    }

    public static CaptureDocument Load(string path) {
        string json = File.ReadAllText(path, Encoding.UTF8);
        return Deserialize(json);
    }

    /// <summary>
    /// Ersetzt fehlende (null) Listen und Werte durch Standardwerte, damit ältere oder handbearbeitete Dateien lesbar bleiben.
    /// </summary>
    private static void normalize(CaptureDocument document) {
        document.Image ??= "";
        document.Monitors ??= new List<MonitorInfo>();
        document.Windows ??= new List<WindowInfo>();
        document.Annotations ??= new List<Annotation>();
        document.Monitors.RemoveAll(m => m == null);
        document.Windows.RemoveAll(w => w == null);
        foreach(MonitorInfo monitor in document.Monitors) monitor.Device ??= "";
        foreach(WindowInfo window in document.Windows) {
            window.Title ??= "";
            window.Process ??= "";
        }
        if(document.Foreground != null) {
            document.Foreground.Title ??= "";
            document.Foreground.Process ??= "";
        }
    }

    private static JsonSerializerOptions createOptions() {
        JsonSerializerOptions options = new JsonSerializerOptions {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            AllowOutOfOrderMetadataProperties = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
            // Umlaute in Fenstertiteln lesbar lassen
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        options.MakeReadOnly(true);
        return options;
    }
}
