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
    private readonly DelayedCaptureController delayed = new DelayedCaptureController();
    private DispatcherTimer? countdownTimer;
    private CountdownOverlay? countdownOverlay;
    private DelayDialog? delayDialog;
    private IntPtr delayPreviousWindow;
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
        startTrayMode(e.Args);
    }

    protected override void OnExit(ExitEventArgs e) {
        try {
            if(AppServices.IsInitialized) AppServices.Workflow.WaitForPendingWrites(TimeSpan.FromSeconds(15));
            retention?.Dispose();
            if(AppServices.IsInitialized) AppServices.Updates.Dispose();
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

    private void startTrayMode(string[] args) {
        if(startViaElevatedTask(args)) {
            Shutdown(0);
            return;
        }
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
        Log.Info($"ScreenKeeper {CommandLineRunner.GetVersion()} gestartet{(ProcessElevation.IsCurrentElevated() == true ? " (mit Adminrechten)" : "")}, Ablage: {AppServices.Storage.RootFolder}");
        checkElevatedAutostart();
        singleInstance.ShowHistoryRequested += (s, e) => ShowHistory();
        singleInstance.StartListening();
        AppServices.Workflow.WriteFailed += (s, failure) => tray?.ShowBalloon(Strings.ErrorTitle, Strings.Format(Strings.ErrorPngWriteFailed, failure.Path, failure.Error.Message), true);

        tray = new TrayIcon();
        tray.SetHotkeys(AppServices.Settings.Settings);
        tray.CaptureRequested += onTrayCapture;
        tray.DelayedCaptureRequested += onTrayDelayedCapture;
        tray.Clicked += (s, e) => cancelDelayed("Klick aufs Tray-Symbol");
        tray.HistoryRequested += (s, e) => ShowHistory();
        tray.SettingsRequested += (s, e) => ShowSettings();
        tray.ExitRequested += (s, e) => exit();
        tray.UpdateRequested += onTrayUpdate;

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
        ensureAgentsFile();
        startUpdates();
        Dispatcher.BeginInvoke(new Action(warmUpEditor), DispatcherPriority.ApplicationIdle);
    }

    /// <summary>
    /// Manueller Start (Startmenü, Installer) bei eingeschaltetem Autostart mit Adminrechten: statt nicht erhöht weiterzulaufen
    /// die Aufgabe starten (erhöht, ohne UAC) und beenden. Klappt das nicht, normal weiterstarten.
    /// </summary>
    private static bool startViaElevatedTask(string[] args) {
        try {
            string? exePath = Environment.ProcessPath;
            if(exePath == null) return false;
            return ElevatedAutostartService.TryStartViaTask(args,
                ProcessElevation.IsCurrentElevated,
                () => {
                    SettingsService settings = new SettingsService();
                    settings.Load();
                    return settings.Settings.ElevatedAutostart;
                },
                () => SingleInstance.IsRunning(),
                () => ElevatedAutostartService.StartTask(exePath));
        } catch(Exception ex) {
            Log.Error(ex, "Start über die Aufgabe mit Adminrechten fehlgeschlagen");
            return false;
        }
    }

    /// <summary>
    /// Autostart mit Adminrechten eingeschaltet: einen (z. B. vom Installer wieder angelegten) Run-Wert entfernen, damit
    /// es keinen Doppelstart gibt, und prüfen, ob die Aufgabe auf diese EXE zeigt (nur Protokoll). Im Hintergrund.
    /// </summary>
    private static void checkElevatedAutostart() {
        AppSettings settings = AppServices.Settings.Settings;
        if(!settings.ElevatedAutostart) return;
        IAutostartService autostart = AppServices.Autostart;
        string? exePath = Environment.ProcessPath;
        Task.Run(() => {
            try {
                if(autostart.IsEnabled) {
                    autostart.SetEnabled(false);
                    Log.Info("Run-Wert entfernt: der Autostart läuft über die Aufgabe mit Adminrechten");
                }
                if(exePath != null) ElevatedAutostartService.LogTaskState(exePath);
            } catch(Exception ex) {
                Log.Error(ex, "Autostart mit Adminrechten konnte nicht geprüft werden");
            }
        });
    }

    /// <summary>
    /// Update-Prüfung im Hintergrund (verzögert, höchstens einmal je 24 h). Ein Update zeigt nur das Tray-Menü, der
    /// Tooltip, die History-Statusleiste und das Einstellungsfenster an – keine Dialoge, keine Benachrichtigungen.
    /// </summary>
    private void startUpdates() {
        UpdateService updates = AppServices.Updates;
        updates.PrepareExit = prepareUpdateExit;
        updates.Exit = exit;
        updates.Changed += (s, e) => tray?.SetUpdate(updates.Available?.Version);
        tray?.SetUpdate(updates.Available?.Version);
        updates.Start();
    }

    /// <summary>
    /// Vor dem Beenden für ein Update: offenen Editor normal schließen (speichert den Stand), Schreibvorgänge abwarten.
    /// </summary>
    private void prepareUpdateExit() {
        AppServices.Workflow.OpenEditor?.Close();
        AppServices.Workflow.WaitForPendingWrites(TimeSpan.FromSeconds(15));
    }

    private async void onTrayUpdate(object? sender, EventArgs e) {
        try {
            await AppServices.Updates.InstallAsync();
        } catch(Exception ex) {
            Log.Error(ex, "Update aus dem Tray-Menü fehlgeschlagen");
        }
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
        appliedHotkeys = settings.GetHotkeySignature();
        return hotkeys.Apply(settings);
    }

    /// <summary>
    /// Einstellungen gespeichert: Hotkeys nur bei Änderung neu registrieren, Aufbewahrung neu bewerten
    /// (die History lädt bei geändertem Ablageordner selbst neu).
    /// </summary>
    private void onSettingsChanged(object? sender, EventArgs e) {
        AppSettings settings = AppServices.Settings.Settings;
        if(hotkeys != null && appliedHotkeys != settings.GetHotkeySignature()) {
            IReadOnlyList<string> problems = applyHotkeys();
            if(problems.Count > 0) tray?.ShowBalloon(Strings.HintTitle, string.Join(Environment.NewLine, problems), true);
        }
        // Tooltip und Menü des Tray-Symbols zeigen die eingestellten Hotkeys
        tray?.SetHotkeys(settings);
        retention?.Reevaluate();
        AppServices.Updates.Reevaluate();
        // Ablageordner kann gewechselt haben
        ensureAgentsFile();
    }

    /// <summary>
    /// AGENTS.md im Ablageordner anlegen bzw. aktualisieren (im Hintergrund; Fehler protokolliert der Dienst selbst).
    /// </summary>
    private static void ensureAgentsFile() {
        IStorageService storage = AppServices.Storage;
        Task.Run(() => {
            try {
                storage.EnsureAgentsFile();
            } catch(Exception ex) {
                Log.Error(ex, "AGENTS.md konnte nicht geprüft werden");
            }
        });
    }

    private void exit() {
        settingsWindow?.Close();
        historyWindow?.CloseForShutdown();
        Shutdown(0);
    }

    private void onHotkeyPressed(object? sender, HotkeyAction action) {
        switch(action) {
            case HotkeyAction.CaptureEdit:
                cancelDelayed("andere Aufnahme");
                captureAndEdit();
                break;
            case HotkeyAction.CaptureSave:
                cancelDelayed("andere Aufnahme");
                captureSaveOnly();
                break;
            case HotkeyAction.ShowHistory: ShowHistory(); break;
            case HotkeyAction.CaptureDelayed: startDelayed(); break;
            case HotkeyAction.CancelDelayed: cancelDelayed("Esc"); break;
        }
    }

    private async void onTrayCapture(object? sender, EventArgs e) {
        cancelDelayed("andere Aufnahme");
        // Kontextmenü erst verschwinden lassen, damit es nicht mit aufgenommen wird
        await Task.Delay(menuCaptureDelayMs);
        captureAndEdit();
    }

    private async void onTrayDelayedCapture(object? sender, EventArgs e) {
        await Task.Delay(menuCaptureDelayMs);
        startDelayed();
    }

    /// <summary>
    /// Delay-Hotkey bzw. Tray-Eintrag: je nach Zustand Dialog öffnen, Countdown starten, Dialog bestätigen oder sofort
    /// aufnehmen. Ist ein Editor offen, wird er wie bei den anderen Aufnahme-Hotkeys nur nach vorne geholt.
    /// </summary>
    private void startDelayed() {
        EditorWindow? editor = AppServices.Workflow.OpenEditor;
        if(!delayed.IsActive && editor != null) {
            Log.Info("Verzögerte Aufnahme übersprungen: Editor ist offen");
            editor.BringToFront();
            return;
        }
        AppSettings settings = AppServices.Settings.Settings;
        applyDelayed(delayed.HotkeyPressed(settings.CaptureDelayAskEachTime, settings.CaptureDelaySeconds));
    }

    private void cancelDelayed(string reason) {
        if(!delayed.IsActive) return;
        Log.Info($"Verzögerte Aufnahme abgebrochen ({reason})");
        applyDelayed(delayed.Cancel());
    }

    private void applyDelayed(DelayedCaptureEffect effect) {
        switch(effect) {
            case DelayedCaptureEffect.ShowDialog:
                showDelayDialog();
                break;
            case DelayedCaptureEffect.ConfirmDialog:
                delayDialog?.Confirm();
                break;
            case DelayedCaptureEffect.CountdownStarted:
                startCountdown();
                break;
            case DelayedCaptureEffect.CountdownTick:
                countdownOverlay?.SetSeconds(delayed.Remaining);
                tray?.SetCountdown(delayed.Remaining);
                break;
            case DelayedCaptureEffect.CaptureNow:
                captureDelayedNow();
                break;
            case DelayedCaptureEffect.Cancelled:
                stopCountdown();
                break;
        }
    }

    /// <summary>
    /// „Jedes Mal fragen“: Vordergrundfenster merken, Dialog zeigen. Nach dem Start geht der Fokus dorthin zurück, damit der
    /// Benutzer in der Countdown-Zeit dort das Menü öffnen kann.
    /// </summary>
    private void showDelayDialog() {
        delayPreviousWindow = User32.GetForegroundWindow();
        DelayDialog dialog = new DelayDialog(delayed.Seconds);
        delayDialog = dialog;
        dialog.Confirmed += (s, seconds) => {
            delayDialog = null;
            rememberDelaySeconds(seconds);
            WindowActivation.ForceForeground(delayPreviousWindow);
            applyDelayed(delayed.DialogConfirmed(seconds));
        };
        dialog.Cancelled += (s, e) => {
            delayDialog = null;
            WindowActivation.ForceForeground(delayPreviousWindow);
            Log.Info("Verzögerte Aufnahme: Dialog abgebrochen");
            applyDelayed(delayed.DialogCancelled());
        };
        dialog.Show();
        bringToFront(dialog);
    }

    /// <summary>Gewählte Verzögerung als neuen Vorschlag speichern.</summary>
    private static void rememberDelaySeconds(int seconds) {
        AppSettings settings = AppServices.Settings.Settings;
        if(settings.CaptureDelaySeconds == seconds) return;
        settings.CaptureDelaySeconds = seconds;
        try {
            AppServices.Settings.Save();
        } catch(Exception ex) {
            Log.Error(ex, "Verzögerung konnte nicht gespeichert werden");
        }
    }

    private void startCountdown() {
        hotkeys?.SetCancelKeyActive(true);
        countdownOverlay = new CountdownOverlay();
        countdownOverlay.SetSeconds(delayed.Remaining);
        countdownOverlay.Show();
        tray?.SetCountdown(delayed.Remaining);
        countdownTimer?.Stop();
        countdownTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (s, e) => applyDelayed(delayed.Tick()), Dispatcher);
        countdownTimer.Start();
        Log.Info($"Verzögerte Aufnahme: Countdown {delayed.Remaining} s");
    }

    /// <summary>Countdown, Overlay, Esc-Abfang und Dialog beenden (ohne Aufnahme).</summary>
    private void stopCountdown() {
        countdownTimer?.Stop();
        countdownTimer = null;
        hotkeys?.SetCancelKeyActive(false);
        tray?.SetCountdown(null);
        countdownOverlay?.Close();
        countdownOverlay = null;
        DelayDialog? dialog = delayDialog;
        delayDialog = null;
        dialog?.Abort();
    }

    /// <summary>
    /// Countdown vorbei (oder erneuter Hotkey): Overlay ausblenden und abwarten, dass es wirklich weg ist, dann aufnehmen
    /// wie per Hotkey (Editor bzw. nur speichern mit Benachrichtigung).
    /// </summary>
    private async void captureDelayedNow() {
        countdownTimer?.Stop();
        countdownTimer = null;
        hotkeys?.SetCancelKeyActive(false);
        tray?.SetCountdown(null);
        CountdownOverlay? overlay = countdownOverlay;
        countdownOverlay = null;
        try {
            if(overlay != null) {
                await overlay.HideForCaptureAsync();
                overlay.Close();
            }
        } catch(Exception ex) {
            Log.Error(ex, "Countdown-Overlay konnte nicht ausgeblendet werden");
        }
        if(AppServices.Settings.Settings.DelayedCaptureAction == DelayedCaptureActions.Save) {
            Log.Info("Verzögerte Aufnahme: nur speichern");
            captureSaveOnly();
        } else {
            Log.Info("Verzögerte Aufnahme: mit Editor");
            captureAndEdit();
        }
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
