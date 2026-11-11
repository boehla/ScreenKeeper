using System.Text;

namespace ScreenKeeper.Services;

/// <summary>
/// Die <c>AGENTS.md</c> im Ablageordner: erklärt KI-Agenten und Skripten Ordnerstruktur und Dateiformat. Der Inhalt ist
/// als Ressource eingebettet (<c>Resources\AGENTS.template.md</c>, Platzhalter <c>{{VERSION}}</c>) und wird nur geschrieben,
/// wenn die Datei fehlt oder ihr Inhalt abweicht – so wird sie mit einer neuen Programmversion überschrieben.
/// </summary>
public static class AgentsFile {
    public const string FileName = "AGENTS.md";

    private const string resourceName = "ScreenKeeper.AGENTS.template.md";
    private const string versionPlaceholder = "{{VERSION}}";

    /// <summary>Inhalt für die laufende Programmversion.</summary>
    public static string BuildContent() {
        return BuildContent(CommandLineRunner.GetVersion());
    }

    /// <summary>Inhalt mit <paramref name="version"/> im Kopf; Zeilenenden CRLF.</summary>
    public static string BuildContent(string version) {
        string template;
        using (Stream? stream = typeof(AgentsFile).Assembly.GetManifestResourceStream(resourceName)) {
            if(stream == null) throw new InvalidOperationException($"Ressource {resourceName} fehlt.");
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8, true)) {
                template = reader.ReadToEnd();
            }
        }
        string text = template.Replace("\r\n", "\n").Replace("\n", "\r\n");
        return text.Replace(versionPlaceholder, version);
    }

    /// <summary>Pfad der Datei im Ablageordner.</summary>
    public static string GetPath(string rootFolder) {
        return Path.Combine(rootFolder, FileName);
    }

    /// <summary>
    /// Schreibt <paramref name="content"/> nach <c>&lt;rootFolder&gt;\AGENTS.md</c> (UTF-8 ohne BOM, atomar), wenn die Datei
    /// fehlt oder anders lautet. Der Ablageordner wird nicht angelegt. True, wenn geschrieben wurde. Wirft bei Fehlern.
    /// </summary>
    public static bool Ensure(string rootFolder, string content) {
        if(!Directory.Exists(rootFolder)) return false;
        string path = GetPath(rootFolder);
        if(File.Exists(path)) {
            string existing = File.ReadAllText(path, Encoding.UTF8);
            if(string.Equals(existing, content, StringComparison.Ordinal)) return false;
        }
        AtomicFile.WriteAllText(path, content);
        return true;
    }

    /// <summary>
    /// Wie <see cref="Ensure"/> mit dem Inhalt der laufenden Version; Fehler werden nur protokolliert (nie den Start oder
    /// eine Aufnahme stören).
    /// </summary>
    public static bool TryEnsure(string rootFolder) {
        try {
            bool written = Ensure(rootFolder, BuildContent());
            if(written) Log.Info($"{FileName} im Ablageordner geschrieben: {GetPath(rootFolder)}");
            return written;
        } catch(Exception ex) {
            Log.Warn($"{FileName} im Ablageordner {rootFolder} konnte nicht geschrieben werden: {ex.Message}");
            return false;
        }
    }
}
