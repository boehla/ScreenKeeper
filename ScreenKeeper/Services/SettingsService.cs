using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ScreenKeeper.Services;

public interface ISettingsService {
    AppSettings Settings { get; }

    string FilePath { get; }

    /// <summary>Wird nach <see cref="Save"/> ausgelöst.</summary>
    event EventHandler? Changed;

    void Load();

    void Save();
}

/// <summary>
/// Lädt und speichert <see cref="AppSettings"/>. Laden ist tolerant (fehlende, unbekannte oder kaputte Werte → Standard),
/// Speichern atomar.
/// </summary>
public sealed class SettingsService : ISettingsService {
    private static readonly JsonSerializerOptions options = new JsonSerializerOptions {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public SettingsService() : this(AppPaths.SettingsFile) {
    }

    public SettingsService(string filePath) {
        FilePath = filePath;
    }

    public AppSettings Settings { get; private set; } = new AppSettings();

    public string FilePath { get; }

    public event EventHandler? Changed;

    public void Load() {
        AppSettings? loaded = null;
        if(File.Exists(FilePath)) {
            try {
                string json = File.ReadAllText(FilePath, Encoding.UTF8);
                loaded = JsonSerializer.Deserialize<AppSettings>(json, options);
            } catch(Exception ex) {
                Log.Error(ex, $"Einstellungen {FilePath} konnten nicht gelesen werden – Standardwerte werden verwendet");
                backupBrokenFile();
            }
        }
        loaded ??= new AppSettings();
        loaded.Normalize();
        Settings = loaded;
    }

    public void Save() {
        Settings.Normalize();
        string json = JsonSerializer.Serialize(Settings, options);
        AtomicFile.WriteAllText(FilePath, json);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void backupBrokenFile() {
        try {
            File.Copy(FilePath, FilePath + ".broken", true);
        } catch(Exception ex) {
            Log.Error(ex, "Sicherung der defekten Einstellungsdatei fehlgeschlagen");
        }
    }
}
