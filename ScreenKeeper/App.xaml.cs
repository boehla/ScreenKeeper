using System.Diagnostics;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;
using ScreenKeeper.Services.Native;
using ScreenKeeper.Views;

namespace ScreenKeeper;

/// <summary>
/// Programmstart ohne StartupUri: entweder Kommandozeilen-Modus (headless, ohne Tray/Mutex)
/// oder Tray-Modus mit Einzelinstanz, Hotkeys und Tray-Symbol.
/// </summary>
public partial class App : Application {
    private const int menuCaptureDelayMs = 300;

    private SingleInstance? singleInstance;
    private TrayIcon? tray;
    private HotkeyService? hotkeys;
    private RetentionService? retention;
    private HistoryWindow? historyWindow;
    private SettingsWindow? settingsWindow;
    private string appliedHotkeys = "";
    private bool headless;

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        DispatcherUnhandledException += onDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += onDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += onUnobservedTaskException;

        if(CommandLineRunner.IsCommandLine(e.Args)) {
            headless = true;
            string[] args = e.Args;
            Dispatcher.InvokeAsync(async () => {
                int exitCode = await new CommandLineRunner().RunAsync(args);
                Shutdown(exitCode);
            });
            return;
        }
        startTrayMode();
    }

    protected override void OnExit(ExitEventArgs e) {
        try {
            if(AppServices.IsInitialized) AppServices.Workflow.WaitForPendingWrites(TimeSpan.FromSeconds(15));
            retention?.Dispose();
            hotkeys?.Dispose();
            tray?.Dispose();
            singleInstance?.Dispose();
        } catch(Exception ex) {
            Log.Error(ex, "Fehler beim Beenden");
        }
        Log.Info($"Beendet (Exit-Code {e.ApplicationExitCode})");
        base.OnExit(e);
    }

    /// <summary>
    /// History-Fenster zeigen bzw. nach vorne holen. Es wird einmal erzeugt und beim Schließen nur versteckt.
    /// </summary>
    public void ShowHistory() {
        if(historyWindow == null) {
            historyWindow = new HistoryWindow(true);
            historyWindow.Closed += (s, e) => historyWindow = null;
        }
        if(!historyWindow.IsVisible) historyWindow.Show();
        bringToFront(historyWindow);
    }

    /// <summary>
    /// Einstellungsfenster zeigen bzw. nach vorne holen (nur ein Fenster gleichzeitig).
    /// </summary>
    public void ShowSettings() {
        if(settingsWindow == null) {
            settingsWindow = new SettingsWindow();
            settingsWindow.Closed += (s, e) => settingsWindow = null;
            settingsWindow.Show();
        }
        bringToFront(settingsWindow);
    }

    private void startTrayMode() {
        singleInstance = new SingleInstance();
        if(!singleInstance.TryAcquire()) {
            Log.Info("Zweiter Start – laufende Instanz wird benachrichtigt");
            SingleInstance.SignalFirstInstance();
            singleInstance.Dispose();
            singleInstance = null;
            Shutdown(0);
            return;
        }

        AppServices.Initialize(null);
        Log.Info($"ScreenKeeper {CommandLineRunner.GetVersion()} gestartet, Ablage: {AppServices.Storage.RootFolder}");
        singleInstance.ShowHistoryRequested += (s, e) => ShowHistory();
        singleInstance.StartListening();
        AppServices.Workflow.WriteFailed += (s, failure) => tray?.ShowBalloon(Strings.ErrorTitle, Strings.Format(Strings.ErrorPngWriteFailed, failure.Path, failure.Error.Message), true);

        tray = new TrayIcon();
        tray.CaptureRequested += onTrayCapture;
        tray.HistoryRequested += (s, e) => ShowHistory();
        tray.SettingsRequested += (s, e) => ShowSettings();
        tray.ExitRequested += (s, e) => exit();

        hotkeys = new HotkeyService();
        AppServices.Hotkeys = hotkeys;
        hotkeys.HotkeyPressed += onHotkeyPressed;

        List<string> hints = new List<string>(AppServices.EnvironmentCheck.Check());
        hints.AddRange(applyHotkeys());
        AppServices.Settings.Changed += onSettingsChanged;
        if(hints.Count > 0) {
            foreach(string hint in hints) Log.Info($"Hinweis: {hint}");
            tray.ShowBalloon(Strings.HintTitle, string.Join(Environment.NewLine, hints), true);
        }

        retention = new RetentionService(AppServices.Storage, AppServices.Settings);
        retention.Completed += (s, result) => historyWindow?.Reload();
        retention.Start();
        Dispatcher.BeginInvoke(new Action(warmUpEditor), DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Editor einmal unsichtbar vorwärmen, damit schon die erste Aufnahme nach dem Start schnell erscheint.
    /// </summary>
    private static async void warmUpEditor() {
        try {
            Stopwatch watch = Stopwatch.StartNew();
            await EditorWindow.WarmUp();
            Log.Info($"Editor vorgewärmt in {watch.ElapsedMilliseconds} ms");
        } catch(Exception ex) {
            Log.Error(ex, "Vorwärmen des Editors fehlgeschlagen");
        }
    }

    private IReadOnlyList<string> applyHotkeys() {
        if(hotkeys == null) return Array.Empty<string>();
        AppSettings settings = AppServices.Settings.Settings;
        appliedHotkeys = $"{settings.HotkeyCaptureEdit}|{settings.HotkeyCaptureSave}|{settings.HotkeyHistory}";
        return hotkeys.Apply(settings);
    }

    /// <summary>
    /// Einstellungen gespeichert: Hotkeys nur bei Änderung neu registrieren, Aufbewahrung neu bewerten
    /// (die History lädt bei geändertem Ablageordner selbst neu).
    /// </summary>
    private void onSettingsChanged(object? sender, EventArgs e) {
        AppSettings settings = AppServices.Settings.Settings;
        if(hotkeys != null && appliedHotkeys != $"{settings.HotkeyCaptureEdit}|{settings.HotkeyCaptureSave}|{settings.HotkeyHistory}") {
            IReadOnlyList<string> problems = applyHotkeys();
            if(problems.Count > 0) tray?.ShowBalloon(Strings.HintTitle, string.Join(Environment.NewLine, problems), true);
        }
        retention?.Reevaluate();
    }

    private void exit() {
        settingsWindow?.Close();
        historyWindow?.CloseForShutdown();
        Shutdown(0);
    }

    private void onHotkeyPressed(object? sender, HotkeyAction action) {
        switch(action) {
            case HotkeyAction.CaptureEdit: captureAndEdit(); break;
            case HotkeyAction.CaptureSave: captureSaveOnly(); break;
            case HotkeyAction.ShowHistory: ShowHistory(); break;
        }
    }

    private async void onTrayCapture(object? sender, EventArgs e) {
        // Kontextmenü erst verschwinden lassen, damit es nicht mit aufgenommen wird
        await Task.Delay(menuCaptureDelayMs);
        captureAndEdit();
    }

    private void captureAndEdit() {
        try {
            AppServices.Workflow.CaptureAndEdit();
        } catch(Exception ex) {
            Log.Error(ex, "Aufnahme mit Editor fehlgeschlagen");
            tray?.ShowBalloon(Strings.ErrorTitle, Strings.Format(Strings.ErrorCaptureFailed, ex.Message), true);
        }
    }

    private void captureSaveOnly() {
        EditorWindow? editor = AppServices.Workflow.OpenEditor;
        if(editor != null) {
            // Eine Aufnahme würde jetzt nur den Editor fotografieren: stattdessen den Editor nach vorne holen
            Log.Info("Aufnahme (nur speichern) übersprungen: Editor ist offen");
            editor.BringToFront();
            return;
        }
        try {
            (_, SavedCapture saved) = AppServices.Workflow.CaptureAndSave();
            if(AppServices.Settings.Settings.NotifyOnSaveOnly) {
                tray?.ShowBalloon(Strings.NotifySavedTitle, Strings.Format(Strings.NotifySavedText, Path.GetFileName(saved.Files.PngPath)), false);
            }
        } catch(Exception ex) {
            Log.Error(ex, "Aufnahme (nur speichern) fehlgeschlagen");
            tray?.ShowBalloon(Strings.ErrorTitle, Strings.Format(Strings.ErrorCaptureFailed, ex.Message), true);
        }
    }

    /// <summary>
    /// Fenster in den Vordergrund holen – auch wenn gerade ein anderes Programm den Fokus hat (Hotkey, Tray, zweiter Start).
    /// </summary>
    private static void bringToFront(Window window) {
        if(window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
        window.Activate();
        WindowActivation.ForceForeground(new WindowInteropHelper(window).Handle);
        window.Focus();
    }

    private void onDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e) {
        Log.Error(e.Exception, "Unbehandelte Ausnahme im UI-Thread");
        e.Handled = true;
        if(headless) {
            Console.Error.WriteLine(e.Exception.Message);
            Shutdown(1);
            return;
        }
        showError(e.Exception);
    }

    private void onDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) {
        Exception error = e.ExceptionObject as Exception ?? new Exception(e.ExceptionObject?.ToString());
        Log.Error(error, $"Unbehandelte Ausnahme (Programmende: {e.IsTerminating})");
        if(headless) {
            Console.Error.WriteLine(error.Message);
            return;
        }
        showError(error);
    }

    private void onUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e) {
        Log.Error(e.Exception, "Unbeobachtete Ausnahme in einem Task");
        e.SetObserved();
    }

    private static void showError(Exception error) {
        try {
            MessageBox.Show(Strings.Format(Strings.ErrorUnhandled, error.Message, Log.FilePath), Strings.ErrorTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        } catch(Exception ex) {
            Log.Error(ex, "Fehlermeldung konnte nicht angezeigt werden");
        }
    }
}
