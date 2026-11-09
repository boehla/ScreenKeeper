using System.Text;

namespace ScreenKeeper.Services;

/// <summary>
/// Atomares Schreiben: erst in eine temporäre Datei daneben, dann per Umbenennen ersetzen.
/// Bei einem Absturz bleibt so nie eine halbe Zieldatei zurück.
/// </summary>
public static class AtomicFile {
    public static void WriteAllText(string path, string text) {
        byte[] bytes = new UTF8Encoding(false).GetBytes(text);
        Write(path, stream => stream.Write(bytes, 0, bytes.Length));
    }

    public static void WriteAllBytes(string path, byte[] bytes) {
        Write(path, stream => stream.Write(bytes, 0, bytes.Length));
    }

    /// <summary>
    /// Schreibt über einen Callback (z. B. für Bild-Encoder) atomar in <paramref name="path"/>.
    /// </summary>
    public static void Write(string path, Action<Stream> writer) {
        string? folder = Path.GetDirectoryName(path);
        if(!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        string temp = path + ".tmp";
        try {
            using (FileStream stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None)) {
                writer(stream);
                stream.Flush(true);
            }
            File.Move(temp, path, true);
        } catch {
            try {
                if(File.Exists(temp)) File.Delete(temp);
            } catch(Exception ex) {
                Log.Error(ex, $"Temporäre Datei {temp} konnte nicht gelöscht werden");
            }
            throw;
        }
    }
}
