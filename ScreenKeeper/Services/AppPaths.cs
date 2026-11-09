namespace ScreenKeeper.Services;

/// <summary>
/// Benutzerbezogene Ablageorte des Programms. Neben die EXE wird nie geschrieben.
/// </summary>
public static class AppPaths {
    /// <summary><c>%APPDATA%\ScreenKeeper</c></summary>
    public static string AppDataFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScreenKeeper");

    public static string SettingsFile => Path.Combine(AppDataFolder, "settings.json");

    public static string LogFile => Path.Combine(AppDataFolder, "log.txt");

    /// <summary>Standard-Ablageordner: Known Folder „Bilder“ \ ScreenKeeper.</summary>
    public static string DefaultStorageFolder => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ScreenKeeper");
}
