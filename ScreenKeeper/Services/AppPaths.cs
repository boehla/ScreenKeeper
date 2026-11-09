namespace ScreenKeeper.Services;

/// <summary>
/// Benutzerbezogene Ablageorte des Programms. Neben die EXE wird nie geschrieben.
/// </summary>
public static class AppPaths {
    /// <summary><c>%APPDATA%\ScreenKeeper</c></summary>
    public static string AppDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenKeeper");

    public static string SettingsFile => Path.Combine(AppDataFolder, "settings.json");

    public static string LogFile => Path.Combine(AppDataFolder, "log.txt");

    /// <summary>Zeitpunkt und Ergebnis der letzten Update-Prüfung (getrennt von den Einstellungen).</summary>
    public static string UpdateStateFile => Path.Combine(AppDataFolder, "update.json");

    /// <summary><c>%LOCALAPPDATA%\ScreenKeeper\update</c>: heruntergeladene Updates.</summary>
    public static string UpdateFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ScreenKeeper", "update");

    /// <summary>Standard-Ablageordner: Known Folder „Bilder“ \ ScreenKeeper.</summary>
    public static string DefaultStorageFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenKeeper");
}
