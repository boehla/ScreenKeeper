using System.Diagnostics;
using System.Text;

namespace ScreenKeeper.Services;

/// <summary>
/// Einfaches Protokoll nach <c>%APPDATA%\ScreenKeeper\log.txt</c>. Wird die Datei größer als 1 MB,
/// wird sie nach <c>log.old.txt</c> verschoben.
/// </summary>
public static class Log {
    private const long maxSize = 1024 * 1024;
    private static readonly object sync = new object();

    /// <summary>Kann (z. B. in Tests) auf eine andere Datei umgelenkt werden; null = Standardpfad.</summary>
    public static string? FileOverride { get; set; }

    public static string FilePath => FileOverride ?? AppPaths.LogFile;

    public static void Info(string message) {
        write("INFO ", message);
    }

    public static void Warn(string message) {
        write("WARN ", message);
    }

    public static void Error(Exception ex, string context) {
        write("ERROR", $"{context}: {ex}");
    }

    private static void write(string level, string message) {
        string line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}";
        Debug.Write(line);
        lock(sync) {
            try {
                string path = FilePath;
                string? folder = Path.GetDirectoryName(path);
                if(!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
                FileInfo info = new FileInfo(path);
                if(info.Exists && info.Length > maxSize) {
                    string old = Path.Combine(info.DirectoryName ?? "", Path.GetFileNameWithoutExtension(path) + ".old" + info.Extension);
                    File.Move(path, old, true);
                }
                File.AppendAllText(path, line, Encoding.UTF8);
            } catch(Exception ex) {
                // Protokoll darf nie selbst einen Fehler auslösen
                Debug.WriteLine($"Log konnte nicht geschrieben werden: {ex.Message}");
            }
        }
    }
}
