using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services.Native;
using ScreenKeeper.ViewModels;
using ScreenKeeper.Views;

namespace ScreenKeeper.Services;

/// <summary>
/// Kommandozeilen-Modus für automatisierte Tests (ohne Tray und Mutex):
/// <c>--capture [--no-editor] [--folder &lt;Ordner&gt;]</c>, <c>--render &lt;json&gt; &lt;out.png&gt; [--region selection|full|monitor:&lt;n&gt;]</c>,
/// <c>--open &lt;json&gt;</c>, <c>--version</c>. Exit-Code 0 = Erfolg, 1 = Fehler (Meldung auf stderr).
/// </summary>
public sealed class CommandLineRunner {
    private const int captureTimingRuns = 3;
    private static readonly string[] switches = { "--capture", "--render", "--open", "--history", "--settings", "--retention", "--check-update", "--version", ElevatedAutostartService.HelperSwitch };

    public static bool IsCommandLine(string[] args) {
        return args.Any(a => switches.Contains(a, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Führt den Befehl aus; läuft im UI-Thread der WPF-Application (STA, für BitmapSource/RenderTargetBitmap/Editor).
    /// </summary>
    public async Task<int> RunAsync(string[] args) {
        attachConsole();
        try {
            string command = args.First(a => switches.Contains(a, StringComparer.OrdinalIgnoreCase)).ToLowerInvariant();
            switch(command) {
                case "--version": return runVersion();
                case "--capture": return await runCapture(args);
                case "--render": return runRender(args);
                case "--open": return await runOpen(args);
                case "--history": return await runHistory(args);
                case "--settings": return await runSettings(args);
                case "--retention": return await runRetention(args);
                case "--check-update": return await runCheckUpdate();
                case ElevatedAutostartService.HelperSwitch: return runElevatedAutostart(args);
            }
            writeError(Strings.CliUsage);
            return 1;
        } catch(Exception ex) {
            Log.Error(ex, $"Kommandozeile: {string.Join(" ", args)}");
            writeError(ex.Message);
            return 1;
        }
    }

    public static string GetVersion() {
        Assembly assembly = typeof(CommandLineRunner).Assembly;
        string? version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if(string.IsNullOrEmpty(version)) version = assembly.GetName().Version?.ToString() ?? "0.0.0";
        int plus = version.IndexOf('+');
        return plus > 0 ? version.Substring(0, plus) : version;
    }

    private static int runVersion() {
        Console.Out.WriteLine(GetVersion());
        return 0;
    }

    private static async Task<int> runCapture(string[] args) {
        bool noEditor = hasFlag(args, "--no-editor");
        string? folder = getOption(args, "--folder");
        AppServices.Initialize(folder);
        // nur für automatisierte Tests, absichtlich nicht in der Hilfe
        if(hasFlag(args, "--test-capture-timing")) return await runCaptureTiming();
        (CaptureResult result, SavedCapture saved) = AppServices.Workflow.CaptureAndSave();
        await Task.WhenAll(saved.PngTask, saved.ThumbnailTask);
        Console.Out.WriteLine(saved.Files.PngPath);
        Console.Out.WriteLine(saved.Files.JsonPath);
        Console.Out.WriteLine(saved.Files.ThumbPath);
        Console.Out.Flush();
        if(!noEditor) {
            EditorWindow editor = AppServices.Workflow.ShowEditor(result.Document, result.Bitmap, saved.Files.JsonPath, EditorMode.Fullscreen);
            await waitForClose(editor);
        }
        return 0;
    }

    /// <summary>
    /// <c>--capture --test-capture-timing [--folder &lt;Ordner&gt;]</c>: mehrmals hintereinander derselbe Weg wie der Hotkey im
    /// Tray-Betrieb (<see cref="CaptureWorkflow.CaptureAndEdit"/>), Zeit bis <c>ContentRendered</c> des Editors auf stdout
    /// (Lauf 1 = kalter Prozess), Editor danach schließen.
    /// </summary>
    private static async Task<int> runCaptureTiming() {
        // wie im Tray-Betrieb: Editor beim Start vorwärmen
        Stopwatch warmUp = Stopwatch.StartNew();
        await EditorWindow.WarmUp();
        Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"warmUpMs={warmUp.ElapsedMilliseconds}"));
        await Task.Delay(500);
        List<double> times = new List<double>();
        for(int run = 1; run <= captureTimingRuns; run++) {
            long start = Stopwatch.GetTimestamp();
            EditorWindow editor = AppServices.Workflow.CaptureAndEdit();
            Task closed = waitForClose(editor);
            TaskCompletionSource<TimeSpan> rendered = new TaskCompletionSource<TimeSpan>();
            editor.ContentRendered += (s, e) => rendered.TrySetResult(Stopwatch.GetElapsedTime(start));
            Task finished = await Task.WhenAny(rendered.Task, Task.Delay(TimeSpan.FromSeconds(10)));
            if(finished != rendered.Task) throw new TimeoutException("Der Editor wurde nicht sichtbar.");
            double milliseconds = rendered.Task.Result.TotalMilliseconds;
            times.Add(milliseconds);
            Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"run={run} visibleMs={milliseconds:0}"));
            await Task.Delay(300);
            editor.Close();
            await closed;
            AppServices.Workflow.WaitForPendingWrites(TimeSpan.FromSeconds(15));
            // Bildschirm neu zeichnen lassen, damit die nächste Aufnahme nicht noch den Editor enthält
            await Task.Delay(700);
        }
        Console.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"min={times.Min():0} max={times.Max():0} warmAvg={times.Skip(1).DefaultIfEmpty(times[0]).Average():0}"));
        Console.Out.Flush();
        return 0;
    }

    private static int runRender(string[] args) {
        int index = indexOf(args, "--render");
        if(index + 2 >= args.Length) throw new ArgumentException(Strings.Format(Strings.CliMissingArgument, "--render"));
        string jsonPath = Path.GetFullPath(args[index + 1]);
        string outPath = Path.GetFullPath(args[index + 2]);
        if(!File.Exists(jsonPath)) throw new FileNotFoundException(Strings.Format(Strings.CliFileNotFound, jsonPath));
        AppServices.Initialize(null);
        CaptureDocument document = AppServices.Storage.LoadDocument(jsonPath);
        string pngPath = AppServices.Storage.ResolveImagePath(document, jsonPath);
        if(!File.Exists(pngPath)) throw new FileNotFoundException(Strings.Format(Strings.CliFileNotFound, pngPath));
        BitmapSource original = AppServices.Storage.LoadOriginal(pngPath);
        string regionText = getOption(args, "--region") ?? (document.Selection.HasValue ? "selection" : "full");
        PixelRect region = ResolveRegion(regionText, document, original.PixelWidth, original.PixelHeight);
        BitmapSource rendered = AppServices.Render.Render(document, original, region);
        ImageFiles.WritePng(rendered, outPath);
        Console.Out.WriteLine(outPath);
        return 0;
    }

    private static async Task<int> runOpen(string[] args) {
        int index = indexOf(args, "--open");
        if(index + 1 >= args.Length) throw new ArgumentException(Strings.Format(Strings.CliMissingArgument, "--open"));
        string jsonPath = Path.GetFullPath(args[index + 1]);
        if(!File.Exists(jsonPath)) throw new FileNotFoundException(Strings.Format(Strings.CliFileNotFound, jsonPath));
        // nur für automatisierte Tests, absichtlich nicht in der Hilfe: --test-select x,y,w,h, --test-annotate und --test-magnifier
        string? testSelection = getOption(args, "--test-select");
        PixelRect? selection = testSelection == null ? null : ParseRect(testSelection);
        bool testAnnotate = hasFlag(args, "--test-annotate");
        bool testMagnifier = hasFlag(args, "--test-magnifier");
        AppServices.Initialize(null);
        EditorWindow editor = AppServices.Workflow.OpenExisting(jsonPath);
        Task closed = waitForClose(editor);
        if(selection.HasValue) editor.RunTestSelection(selection.Value);
        if(testAnnotate) editor.RunTestAnnotate();
        if(testMagnifier) editor.RunTestMagnifier();
        await closed;
        return editor.TestFailed ? 1 : 0;
    }

    /// <summary>
    /// <c>--history [--folder &lt;Ablageordner&gt;]</c> öffnet das History-Fenster (Schließen beendet). Nur für automatisierte
    /// Tests, absichtlich nicht in der Hilfe: <c>--test-history</c> (Kennzahlen auf stdout) und
    /// <c>--test-edit &lt;Name&gt;</c> (Bearbeiten-Befehl, Editor nach 1 s schließen, Eintrag aktualisiert?) und
    /// <c>--test-delete &lt;Name&gt;</c> (Aufnahme über den Löschen-Befehl der History in den Papierkorb).
    /// </summary>
    private static async Task<int> runHistory(string[] args) {
        Stopwatch watch = Stopwatch.StartNew();
        string? folder = getOption(args, "--folder");
        bool testHistory = hasFlag(args, "--test-history");
        string? editName = getOption(args, "--test-edit");
        string? deleteName = getOption(args, "--test-delete");
        string? duplicateName = getOption(args, "--test-duplicate");
        string? permanentName = getOption(args, "--test-permanent");
        AppServices.Initialize(folder);
        BindingErrorListener? bindingErrors = testHistory ? BindingErrorListener.Attach() : null;
        HistoryWindow window = new HistoryWindow(false);
        if(testHistory || editName != null || deleteName != null || duplicateName != null || permanentName != null) {
            window.ShowActivated = false;
            window.Show();
            int result = await window.RunTestAsync(watch, testHistory, editName, deleteName, duplicateName, permanentName);
            if(bindingErrors != null) reportBindingErrors(bindingErrors);
            return result;
        }
        Task closed = waitForClose(window);
        window.Show();
        await closed;
        return 0;
    }

    /// <summary>
    /// <c>--settings</c> öffnet das Einstellungsfenster (Schließen beendet). Nur für automatisierte Tests:
    /// <c>--test-settings</c> lädt alle Seiten, gibt Kennzahlen und Bindungsfehler aus und schließt ohne zu speichern.
    /// </summary>
    private static async Task<int> runSettings(string[] args) {
        bool test = hasFlag(args, "--test-settings");
        AppServices.Initialize(null);
        BindingErrorListener? bindingErrors = test ? BindingErrorListener.Attach() : null;
        SettingsWindow window = new SettingsWindow();
        Task closed = waitForClose(window);
        if(!test) {
            window.Show();
            await closed;
            return 0;
        }
        window.ShowActivated = false;
        window.Show();
        int tabs = await window.RunTestAsync();
        SettingsViewModel viewModel = window.ViewModel;
        Console.Out.WriteLine($"tabs={tabs} storage={viewModel.ResolvedStorageFolder} export={viewModel.ResolvedExportFolder} preview={viewModel.FileNamePreview}");
        Console.Out.WriteLine($"hotkeys={viewModel.HotkeyCaptureEdit}|{viewModel.HotkeyCaptureSave}|{viewModel.HotkeyHistory}|{viewModel.HotkeyCaptureDelayed}|{viewModel.HotkeyRemotePrintScreen} status={viewModel.HotkeyCaptureEditStatus} {viewModel.PrintScreenStatus} autostart={viewModel.Autostart}");
        Console.Out.WriteLine($"remote={viewModel.CaptureHotkeysInRemoteSessions} delay={viewModel.CaptureDelaySecondsText} ask={viewModel.CaptureDelayAskEachTime} after={viewModel.DelayedCaptureAction}");
        window.Close();
        await closed;
        if(bindingErrors != null) reportBindingErrors(bindingErrors);
        return 0;
    }

    /// <summary>
    /// <c>--retention --folder &lt;Ablageordner&gt; [--days &lt;n&gt;] [--dry-run]</c> (nur für Tests): ein Aufräum-Lauf wie im
    /// Tray-Betrieb (Standard: Tage aus den Einstellungen), Ergebnis auf stdout.
    /// </summary>
    private static async Task<int> runRetention(string[] args) {
        string? folder = getOption(args, "--folder");
        if(folder == null) throw new ArgumentException(Strings.Format(Strings.CliMissingArgument, "--folder"));
        string? daysText = getOption(args, "--days");
        bool dryRun = hasFlag(args, "--dry-run");
        AppServices.Initialize(folder);
        int days = AppServices.Settings.Settings.RetentionDays;
        if(daysText != null && !int.TryParse(daysText, NumberStyles.Integer, CultureInfo.InvariantCulture, out days)) throw new ArgumentException(Strings.Format(Strings.CliMissingArgument, "--days"));
        RetentionService retention = new RetentionService(AppServices.Storage, AppServices.Settings);
        RetentionResult result = await StaTask.Run(() => retention.Run(DateTime.Now, days, dryRun));
        int left = result.Expired.Count(e => File.Exists(e.Files.PngPath) || File.Exists(e.Files.JsonPath) || File.Exists(e.Files.ThumbPath));
        Console.Out.WriteLine($"days={result.Days} cutoff={result.Cutoff:yyyy-MM-dd HH:mm} expired={result.Expired.Count} keptPermanent={result.Kept} dryRun={result.DryRun} filesLeft={left}");
        Console.Out.Flush();
        return 0;
    }

    /// <summary>
    /// <c>--check-update</c> (nur für Tests): Update-Prüfung ohne Merken, Ausgabe
    /// <c>current=&lt;v&gt; latest=&lt;v&gt; asset=&lt;name|none&gt;</c> und <c>update=&lt;v|none&gt;</c>.
    /// </summary>
    private static async Task<int> runCheckUpdate() {
        AppServices.Initialize(null);
        UpdateCheckResult? result = await AppServices.Updates.CheckAsync(false);
        if(result == null) {
            writeError(AppServices.Updates.CheckResultText);
            return 1;
        }
        Console.Out.WriteLine($"current={result.CurrentVersion} latest={result.Latest.Version} asset={result.Latest.AssetName ?? "none"}");
        Console.Out.WriteLine($"update={(result.IsNewer ? result.Latest.Version : "none")} singleFile={UpdateService.IsSingleFile} selfContained={UpdateService.IsSelfContained}");
        Console.Out.Flush();
        return 0;
    }

    /// <summary>
    /// <c>--elevated-autostart on|off --sid &lt;SID&gt; --exe &lt;Pfad&gt;</c>: erhöhter Hilfsprozess der Einstellungen
    /// (absichtlich nicht in der Hilfe), legt die Aufgabe für den Autostart mit Adminrechten an bzw. löscht sie.
    /// </summary>
    private static int runElevatedAutostart(string[] args) {
        ElevatedAutostartRequest request = ElevatedAutostartService.ParseHelperArguments(args);
        ElevatedAutostartService.RunHelper(request);
        return 0;
    }

    private static void reportBindingErrors(BindingErrorListener listener) {
        Console.Out.WriteLine($"bindingErrors={listener.Messages.Count}");
        foreach(string message in listener.Messages.Take(10)) Console.Error.WriteLine(message);
        Console.Out.Flush();
    }

    /// <summary>
    /// <c>x,y,w,h</c> (ganze Zahlen) in ein Rechteck umsetzen.
    /// </summary>
    public static PixelRect ParseRect(string text) {
        string[] parts = text.Split(',', StringSplitOptions.TrimEntries);
        int[] values = new int[4];
        if(parts.Length != 4) throw new ArgumentException(Strings.Format(Strings.CliInvalidRegion, text));
        for(int i = 0; i < 4; i++) {
            if(!int.TryParse(parts[i], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out values[i])) throw new ArgumentException(Strings.Format(Strings.CliInvalidRegion, text));
        }
        if(values[2] <= 0 || values[3] <= 0) throw new ArgumentException(Strings.Format(Strings.CliInvalidRegion, text));
        return new PixelRect(values[0], values[1], values[2], values[3]);
    }

    /// <summary>
    /// <c>selection</c>, <c>full</c> oder <c>monitor:&lt;index&gt;</c> in ein Rechteck (Bildpixel) umsetzen.
    /// </summary>
    public static PixelRect ResolveRegion(string text, CaptureDocument document, int imageWidth, int imageHeight) {
        string value = text.Trim().ToLowerInvariant();
        if(value == "full") return new PixelRect(0, 0, imageWidth, imageHeight);
        if(value == "selection") {
            if(!document.Selection.HasValue) throw new ArgumentException(Strings.CliNoSelection);
            return document.Selection.Value;
        }
        if(value.StartsWith("monitor:", StringComparison.Ordinal)
            && int.TryParse(value.AsSpan("monitor:".Length), NumberStyles.None, CultureInfo.InvariantCulture, out int monitorIndex)
            && monitorIndex >= 0 && monitorIndex < document.Monitors.Count) {
            return document.Monitors[monitorIndex].Bounds;
        }
        throw new ArgumentException(Strings.Format(Strings.CliInvalidRegion, text));
    }

    private static Task waitForClose(Window window) {
        TaskCompletionSource closed = new TaskCompletionSource();
        window.Closed += (s, e) => closed.TrySetResult();
        return closed.Task;
    }

    private static bool hasFlag(string[] args, string flag) {
        return indexOf(args, flag) >= 0;
    }

    private static string? getOption(string[] args, string name) {
        int index = indexOf(args, name);
        if(index < 0) return null;
        if(index + 1 >= args.Length) throw new ArgumentException(Strings.Format(Strings.CliMissingArgument, name));
        return args[index + 1];
    }

    private static int indexOf(string[] args, string name) {
        for(int i = 0; i < args.Length; i++) {
            if(string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase)) return i;
        }
        return -1;
    }

    private static void writeError(string message) {
        Console.Error.WriteLine(message);
        Console.Error.Flush();
    }

    /// <summary>
    /// Die EXE ist eine Windows-Anwendung ohne eigene Konsole. Ist stdout nicht umgeleitet, an die Konsole des
    /// Aufrufers anhängen, damit Ausgaben sichtbar sind.
    /// </summary>
    private static void attachConsole() {
        IntPtr output = Kernel32.GetStdHandle(Kernel32.STD_OUTPUT_HANDLE);
        if(output == IntPtr.Zero || output == new IntPtr(-1)) {
            Kernel32.AttachConsole(Kernel32.ATTACH_PARENT_PROCESS);
        }
    }
}
