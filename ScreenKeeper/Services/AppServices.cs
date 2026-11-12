namespace ScreenKeeper.Services;

/// <summary>
/// Komposition der Services (ohne DI-Framework). Wird in <c>App.OnStartup</c> bzw. vom Kommandozeilen-Modus befüllt.
/// </summary>
public static class AppServices {
    private static ISettingsService? settings;
    private static IStorageService? storage;
    private static ICaptureService? capture;
    private static IRenderService? render;
    private static IClipboardService? clipboard;
    private static IAutostartService? autostart;
    private static IElevatedAutostartService? elevatedAutostart;
    private static IEnvironmentCheckService? environmentCheck;
    private static CaptureWorkflow? workflow;
    private static UpdateService? updates;

    public static ISettingsService Settings => settings ?? throw notInitialized();

    public static IStorageService Storage => storage ?? throw notInitialized();

    public static ICaptureService Capture => capture ?? throw notInitialized();

    public static IRenderService Render => render ?? throw notInitialized();

    public static IClipboardService Clipboard => clipboard ?? throw notInitialized();

    public static IAutostartService Autostart => autostart ?? throw notInitialized();

    public static IElevatedAutostartService ElevatedAutostart => elevatedAutostart ?? throw notInitialized();

    public static IEnvironmentCheckService EnvironmentCheck => environmentCheck ?? throw notInitialized();

    public static CaptureWorkflow Workflow => workflow ?? throw notInitialized();

    /// <summary>Update-Prüfung (der Timer läuft nur im Tray-Modus, siehe <see cref="UpdateService.Start"/>).</summary>
    public static UpdateService Updates => updates ?? throw notInitialized();

    /// <summary>Nur im Tray-Modus vorhanden (im Kommandozeilen-Modus null).</summary>
    public static IHotkeyService? Hotkeys { get; set; }

    public static bool IsInitialized => settings != null;

    /// <summary>
    /// Erzeugt alle Services. <paramref name="storageFolderOverride"/> ersetzt den Ablageordner (Kommandozeile <c>--folder</c>).
    /// </summary>
    public static void Initialize(string? storageFolderOverride) {
        SettingsService settingsService = new SettingsService();
        settingsService.Load();
        settings = settingsService;
        render = new RenderService();
        storage = new StorageService(settingsService, render) { RootFolderOverride = storageFolderOverride };
        capture = new CaptureService();
        clipboard = new ClipboardService();
        autostart = new AutostartService();
        elevatedAutostart = new ElevatedAutostartService();
        environmentCheck = new EnvironmentCheckService();
        workflow = new CaptureWorkflow();
        updates = new UpdateService(settingsService);
    }

    private static InvalidOperationException notInitialized() {
        return new InvalidOperationException("AppServices.Initialize wurde noch nicht aufgerufen.");
    }
}
