using System.Globalization;
using System.Windows.Input;
using System.Windows.Media;
using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Rückfragen, Ordnerwahl und Fehlermeldungen des Einstellungsfensters.
/// </summary>
public interface ISettingsHost {
    bool Confirm(string message);

    string? PickFolder(string title, string initialFolder);

    void ShowError(string message);
}

/// <summary>Farbfeld der Palette.</summary>
public sealed record ColorChoice(string Hex, Brush Brush);

/// <summary>Auswahlwert mit Anzeigetext.</summary>
public sealed record NamedValue(string Value, string Label);

/// <summary>
/// Einstellungen bearbeiten: Werte werden aus <see cref="AppSettings"/> kopiert und erst mit OK/Übernehmen geprüft und
/// zurückgeschrieben (Speichern löst <see cref="ISettingsService.Changed"/> aus – alles wirkt ohne Neustart).
/// </summary>
public sealed class SettingsViewModel : ViewModelBase {
    public const int MagnifierSizeMin = 80;
    public const int MagnifierSizeMax = 400;

    private readonly ISettingsService settings;
    private readonly IAutostartService autostart;
    private readonly IHotkeyService? hotkeys;
    private readonly Func<bool> isSnippingEnabled;
    private readonly Func<DateTime> clock;

    private string storageFolder = "";
    private string fileNamePattern = "";
    private string exportFolder = "";
    private string retentionDaysText = "0";
    private bool autostartEnabled;
    private bool notifyOnSaveOnly;
    private bool checkForUpdates;
    private string hotkeyCaptureEdit = "";
    private string hotkeyCaptureSave = "";
    private string hotkeyHistory = "";
    private string hotkeyCaptureDelayed = "";
    private string hotkeyRemotePrintScreen = "";
    private bool captureHotkeysInRemoteSessions;
    private string captureDelaySecondsText = "";
    private bool captureDelayAskEachTime;
    private string delayedCaptureAction = "";
    private int magnifierZoom;
    private int magnifierSize;
    private int loadedMagnifierZoom;
    private int loadedMagnifierSize;
    private bool showMagnifierKeyHints;
    private string defaultColor = "";
    private string highlightColor = "";
    private double defaultThickness;
    private double defaultFontSize;
    private int defaultBlockSize;
    private int defaultBlurRadius;
    private double defaultStampSize;
    private string defaultCursorKind = "";
    private string defaultFillColor = "";
    private bool defaultTextBackground;
    private bool defaultSmooth;
    private bool cursorVisible;
    private string hotkeyMessages = "";
    private bool hasHotkeyErrors;
    private string savedMessage = "";

    public SettingsViewModel(ISettingsService settings, IAutostartService autostart, IHotkeyService? hotkeys, Func<bool> isSnippingEnabled, Func<DateTime>? clock = null) {
        this.settings = settings;
        this.autostart = autostart;
        this.hotkeys = hotkeys;
        this.isSnippingEnabled = isSnippingEnabled;
        this.clock = clock ?? (() => DateTime.Now);
        load(settings.Settings);
        settings.Changed += onSettingsChanged;

        Palette = buildPalette(defaultColor);
        HighlightPalette = buildPalette(highlightColor);
        FillPalette = buildFillPalette(defaultFillColor);
        Thicknesses = withValue(ToolOptions.Thicknesses, defaultThickness);
        FontSizes = withValue(ToolOptions.FontSizes, defaultFontSize);
        BlockSizes = withValue(ToolOptions.BlockSizes, defaultBlockSize);
        BlurRadii = withValue(ToolOptions.BlurRadii, defaultBlurRadius);
        StampSizes = withValue(ToolOptions.StampSizes, defaultStampSize);
        CursorKindChoices = new[] {
            new NamedValue(CursorKinds.Captured, Strings.CursorKindCaptured),
            new NamedValue(CursorKinds.Arrow, Strings.CursorKindArrow),
            new NamedValue(CursorKinds.Hand, Strings.CursorKindHand),
            new NamedValue(CursorKinds.IBeam, Strings.CursorKindIBeam)
        };

        DelayedCaptureActionChoices = new[] {
            new NamedValue(DelayedCaptureActions.Edit, Strings.DelayedActionEdit),
            new NamedValue(DelayedCaptureActions.Save, Strings.DelayedActionSave)
        };

        OkCommand = new RelayCommand(() => {
            if(Apply()) CloseRequested?.Invoke(this, EventArgs.Empty);
        });
        ApplyCommand = new RelayCommand(() => Apply());
        CancelCommand = new RelayCommand(() => CloseRequested?.Invoke(this, EventArgs.Empty));
        BrowseStorageCommand = new RelayCommand(() => {
            string? folder = Host?.PickFolder(Strings.SettingsStorageFolder, ResolvedStorageFolder);
            if(folder != null) StorageFolder = folder;
        });
        ResetStorageCommand = new RelayCommand(() => StorageFolder = "");
        BrowseExportCommand = new RelayCommand(() => {
            string? folder = Host?.PickFolder(Strings.SettingsExportFolder, ResolvedExportFolder);
            if(folder != null) ExportFolder = folder;
        });
        ResetExportCommand = new RelayCommand(() => ExportFolder = "");
        ResetPatternCommand = new RelayCommand(() => FileNamePattern = AppSettings.DefaultFileNamePattern);
        ClearHotkeyCommand = new RelayCommand(p => {
            if(p is string name && Enum.TryParse(name, out HotkeyAction action)) SetHotkey(action, "");
        });
    }

    public ISettingsHost? Host { get; set; }

    /// <summary>OK oder Abbrechen: Fenster schließen.</summary>
    public event EventHandler? CloseRequested;

    public ICommand OkCommand { get; }

    public ICommand ApplyCommand { get; }

    public ICommand CancelCommand { get; }

    public ICommand BrowseStorageCommand { get; }

    public ICommand ResetStorageCommand { get; }

    public ICommand BrowseExportCommand { get; }

    public ICommand ResetExportCommand { get; }

    public ICommand ResetPatternCommand { get; }

    public ICommand ClearHotkeyCommand { get; }

    public IReadOnlyList<ColorChoice> Palette { get; }

    public IReadOnlyList<ColorChoice> HighlightPalette { get; }

    /// <summary>Füllfarben: keine (Hex leer), Palette deckend und halbtransparent.</summary>
    public IReadOnlyList<ColorChoice> FillPalette { get; }

    public IReadOnlyList<double> Thicknesses { get; }

    public IReadOnlyList<double> FontSizes { get; }

    public IReadOnlyList<int> BlockSizes { get; }

    public IReadOnlyList<int> BlurRadii { get; }

    public IReadOnlyList<double> StampSizes { get; }

    public IReadOnlyList<NamedValue> CursorKindChoices { get; }

    public IReadOnlyList<NamedValue> DelayedCaptureActionChoices { get; }

    public string SettingsFilePath => settings.FilePath;

    public string PatternHelp => Strings.SettingsPatternHelp;

    // Allgemein

    public string StorageFolder {
        get => storageFolder;
        set {
            if(!SetProperty(ref storageFolder, value ?? "")) return;
            OnPropertyChanged(nameof(ResolvedStorageFolder));
            OnPropertyChanged(nameof(StorageFolderError));
            OnPropertyChanged(nameof(ResolvedExportFolder));
            OnPropertyChanged(nameof(ExportFolderError));
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    public string? StorageFolderError => ValidateFolder(storageFolder);

    /// <summary>Tatsächlich verwendeter Ablageordner.</summary>
    public string ResolvedStorageFolder {
        get {
            if(StorageFolderError != null) return "";
            return StorageService.ResolveStorageFolder(storageFolder);
        }
    }

    public string FileNamePattern {
        get => fileNamePattern;
        set {
            if(!SetProperty(ref fileNamePattern, value ?? "")) return;
            OnPropertyChanged(nameof(FileNamePatternError));
            OnPropertyChanged(nameof(FileNamePreview));
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    public string? FileNamePatternError => CaptureFileName.Validate(fileNamePattern);

    /// <summary>Vorschau des Dateinamens mit der aktuellen Zeit.</summary>
    public string FileNamePreview {
        get {
            if(FileNamePatternError != null) return "";
            return Strings.Format(Strings.SettingsPatternPreview, StorageService.FormatFileName(fileNamePattern, clock()) + CaptureFiles.PngExtension);
        }
    }

    public string ExportFolder {
        get => exportFolder;
        set {
            if(!SetProperty(ref exportFolder, value ?? "")) return;
            OnPropertyChanged(nameof(ResolvedExportFolder));
            OnPropertyChanged(nameof(ExportFolderError));
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    public string? ExportFolderError => ValidateFolder(exportFolder);

    public string ResolvedExportFolder {
        get {
            if(ExportFolderError != null || StorageFolderError != null) return "";
            return StorageService.ResolveExportFolder(exportFolder, StorageService.ResolveStorageFolder(storageFolder));
        }
    }

    public string RetentionDaysText {
        get => retentionDaysText;
        set {
            if(!SetProperty(ref retentionDaysText, value ?? "")) return;
            OnPropertyChanged(nameof(RetentionError));
            OnPropertyChanged(nameof(RetentionInfo));
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    public string? RetentionError => TryParseRetention(retentionDaysText, out _) ? null : Strings.Format(Strings.SettingsRetentionInvalid, AppSettings.MaxRetentionDays);

    public string RetentionInfo {
        get {
            if(!TryParseRetention(retentionDaysText, out int days)) return "";
            return days == 0 ? Strings.SettingsRetentionOff : Strings.Format(Strings.SettingsRetentionOn, days);
        }
    }

    public bool Autostart {
        get => autostartEnabled;
        set => SetProperty(ref autostartEnabled, value);
    }

    public bool NotifyOnSaveOnly {
        get => notifyOnSaveOnly;
        set => SetProperty(ref notifyOnSaveOnly, value);
    }

    /// <summary>Einmal täglich im Hintergrund nach Updates suchen.</summary>
    public bool CheckForUpdates {
        get => checkForUpdates;
        set => SetProperty(ref checkForUpdates, value);
    }

    /// <summary>Update-Anzeige mit „Jetzt prüfen“ (vom Fenster gesetzt; null = keine).</summary>
    public UpdateViewModel? Update { get; set; }

    // Hotkeys

    public string HotkeyCaptureEdit {
        get => hotkeyCaptureEdit;
        set => SetHotkey(HotkeyAction.CaptureEdit, value);
    }

    public string HotkeyCaptureSave {
        get => hotkeyCaptureSave;
        set => SetHotkey(HotkeyAction.CaptureSave, value);
    }

    public string HotkeyHistory {
        get => hotkeyHistory;
        set => SetHotkey(HotkeyAction.ShowHistory, value);
    }

    public string HotkeyCaptureDelayed {
        get => hotkeyCaptureDelayed;
        set => SetHotkey(HotkeyAction.CaptureDelayed, value);
    }

    /// <summary>Im Remotedesktop ein reines Druck an die Sitzung durchreichen (leer = aus).</summary>
    public string HotkeyRemotePrintScreen {
        get => hotkeyRemotePrintScreen;
        set => SetHotkey(HotkeyAction.RemotePrintScreen, value);
    }

    /// <summary>Hotkeys auch bei aktivem Remotedesktop-Fenster abfangen.</summary>
    public bool CaptureHotkeysInRemoteSessions {
        get => captureHotkeysInRemoteSessions;
        set => SetProperty(ref captureHotkeysInRemoteSessions, value);
    }

    public string CaptureDelaySecondsText {
        get => captureDelaySecondsText;
        set {
            if(!SetProperty(ref captureDelaySecondsText, value ?? "")) return;
            OnPropertyChanged(nameof(CaptureDelaySecondsError));
            OnPropertyChanged(nameof(HasErrors));
        }
    }

    public string? CaptureDelaySecondsError => TryParseDelaySeconds(captureDelaySecondsText, out _) ? null : Strings.Format(Strings.SettingsDelaySecondsInvalid, AppSettings.MinCaptureDelaySeconds, AppSettings.MaxCaptureDelaySeconds);

    public bool CaptureDelayAskEachTime {
        get => captureDelayAskEachTime;
        set => SetProperty(ref captureDelayAskEachTime, value);
    }

    /// <summary>Nach dem Countdown: <see cref="DelayedCaptureActions.Edit"/> oder <see cref="DelayedCaptureActions.Save"/>.</summary>
    public string DelayedCaptureAction {
        get => delayedCaptureAction;
        set => SetProperty(ref delayedCaptureAction, value ?? DelayedCaptureActions.Edit);
    }

    public string HotkeyCaptureEditStatus => getStatusText(HotkeyAction.CaptureEdit);

    public string HotkeyCaptureSaveStatus => getStatusText(HotkeyAction.CaptureSave);

    public string HotkeyHistoryStatus => getStatusText(HotkeyAction.ShowHistory);

    public string HotkeyCaptureDelayedStatus => getStatusText(HotkeyAction.CaptureDelayed);

    public string HotkeyRemotePrintScreenStatus => getStatusText(HotkeyAction.RemotePrintScreen);

    /// <summary>„Status Druck-Taste: über RegisterHotKey / über Tastatur-Hook / nicht verfügbar“.</summary>
    public string PrintScreenStatus => Strings.Format(Strings.SettingsPrintStatus, getPrintScreenState());

    /// <summary>Hinweis, wenn Windows die Druck-Taste für das Bildschirmausschnitt-Tool verwendet (sonst leer).</summary>
    public string SnippingHint => isSnippingEnabled() ? Strings.HintSnippingEnabled : "";

    /// <summary>Fehler und Hinweise der letzten Hotkey-Prüfung.</summary>
    public string HotkeyMessages {
        get => hotkeyMessages;
        private set => SetProperty(ref hotkeyMessages, value);
    }

    public bool HasHotkeyErrors {
        get => hasHotkeyErrors;
        private set => SetProperty(ref hasHotkeyErrors, value);
    }

    // Editor

    public int MagnifierZoom {
        get => magnifierZoom;
        set => SetProperty(ref magnifierZoom, Math.Clamp(value, AppSettings.MinMagnifierZoom, AppSettings.MaxMagnifierZoom));
    }

    public int MagnifierSize {
        get => magnifierSize;
        set => SetProperty(ref magnifierSize, Math.Clamp(value, AppSettings.MinMagnifierSize, AppSettings.MaxMagnifierSize));
    }

    /// <summary>Unter der Lupe die Tastenhilfe (Rad: Zoom, +/-: Größe) anzeigen.</summary>
    public bool ShowMagnifierKeyHints {
        get => showMagnifierKeyHints;
        set => SetProperty(ref showMagnifierKeyHints, value);
    }

    /// <summary>
    /// Vom Fenster beim Schließen: nicht mehr auf Änderungen der Einstellungen hören.
    /// </summary>
    public void Detach() {
        settings.Changed -= onSettingsChanged;
    }

    /// <summary>
    /// Die Einstellungen wurden gespeichert (auch von außen, z. B. vom Editor nach dem Verstellen der Lupe): Lupen-Zoom und
    /// -Größe nachziehen, wenn sie sich seit dem Laden geändert haben – sonst schriebe „Übernehmen“ die alten Werte zurück.
    /// Ungespeicherte Änderungen des Benutzers an den Schiebereglern bleiben, solange die Datei denselben Wert hat.
    /// </summary>
    private void onSettingsChanged(object? sender, EventArgs e) {
        AppSettings source = settings.Settings;
        if(source.MagnifierZoom != loadedMagnifierZoom) {
            loadedMagnifierZoom = source.MagnifierZoom;
            MagnifierZoom = source.MagnifierZoom;
        }
        if(source.MagnifierSize != loadedMagnifierSize) {
            loadedMagnifierSize = source.MagnifierSize;
            MagnifierSize = source.MagnifierSize;
        }
    }

    public string DefaultColor {
        get => defaultColor;
        set => SetProperty(ref defaultColor, value ?? defaultColor);
    }

    public string HighlightColor {
        get => highlightColor;
        set => SetProperty(ref highlightColor, value ?? highlightColor);
    }

    public double DefaultThickness {
        get => defaultThickness;
        set => SetProperty(ref defaultThickness, value);
    }

    public double DefaultFontSize {
        get => defaultFontSize;
        set => SetProperty(ref defaultFontSize, value);
    }

    public int DefaultBlockSize {
        get => defaultBlockSize;
        set => SetProperty(ref defaultBlockSize, value);
    }

    public int DefaultBlurRadius {
        get => defaultBlurRadius;
        set => SetProperty(ref defaultBlurRadius, value);
    }

    public double DefaultStampSize {
        get => defaultStampSize;
        set => SetProperty(ref defaultStampSize, value);
    }

    public string DefaultCursorKind {
        get => defaultCursorKind;
        set => SetProperty(ref defaultCursorKind, value ?? defaultCursorKind);
    }

    /// <summary>Standard-Füllfarbe für Rechteck und Ellipse; leer = keine Füllung.</summary>
    public string DefaultFillColor {
        get => defaultFillColor;
        set => SetProperty(ref defaultFillColor, value ?? "");
    }

    public bool DefaultTextBackground {
        get => defaultTextBackground;
        set => SetProperty(ref defaultTextBackground, value);
    }

    public bool DefaultSmooth {
        get => defaultSmooth;
        set => SetProperty(ref defaultSmooth, value);
    }

    public bool CursorVisible {
        get => cursorVisible;
        set => SetProperty(ref cursorVisible, value);
    }

    /// <summary>Meldung nach erfolgreichem Übernehmen.</summary>
    public string SavedMessage {
        get => savedMessage;
        private set => SetProperty(ref savedMessage, value);
    }

    public bool HasErrors => StorageFolderError != null || ExportFolderError != null || FileNamePatternError != null || RetentionError != null || CaptureDelaySecondsError != null;

    public string GetHotkey(HotkeyAction action) {
        switch(action) {
            case HotkeyAction.CaptureEdit: return hotkeyCaptureEdit;
            case HotkeyAction.CaptureSave: return hotkeyCaptureSave;
            case HotkeyAction.ShowHistory: return hotkeyHistory;
            case HotkeyAction.CaptureDelayed: return hotkeyCaptureDelayed;
            case HotkeyAction.RemotePrintScreen: return hotkeyRemotePrintScreen;
        }
        return "";
    }

    public void SetHotkey(HotkeyAction action, string? text) {
        string value = text ?? "";
        bool changed;
        switch(action) {
            case HotkeyAction.CaptureEdit:
                changed = SetProperty(ref hotkeyCaptureEdit, value, nameof(HotkeyCaptureEdit));
                break;
            case HotkeyAction.CaptureSave:
                changed = SetProperty(ref hotkeyCaptureSave, value, nameof(HotkeyCaptureSave));
                break;
            case HotkeyAction.ShowHistory:
                changed = SetProperty(ref hotkeyHistory, value, nameof(HotkeyHistory));
                break;
            case HotkeyAction.CaptureDelayed:
                changed = SetProperty(ref hotkeyCaptureDelayed, value, nameof(HotkeyCaptureDelayed));
                break;
            case HotkeyAction.RemotePrintScreen:
                changed = SetProperty(ref hotkeyRemotePrintScreen, value, nameof(HotkeyRemotePrintScreen));
                break;
            default:
                return;
        }
        if(!changed) return;
        // Sofort-Prüfung ohne Probe-Registrierung (die erfolgt beim Speichern)
        showHotkeyIssues(HotkeyConflictChecker.Check(getBindings(), null, false));
    }

    /// <summary>
    /// Prüfen und speichern. False bei Fehlern (inline angezeigt) oder wenn der Benutzer bei Hinweisen abbricht.
    /// </summary>
    public bool Apply() {
        SavedMessage = "";
        OnPropertyChanged(nameof(HasErrors));
        if(HasErrors) return false;
        List<HotkeyIssue> issues = HotkeyConflictChecker.Check(getBindings(), probe, isSnippingEnabled());
        showHotkeyIssues(issues);
        if(issues.Any(i => i.Severity == HotkeyIssueSeverity.Error)) return false;
        List<string> warnings = issues.Where(i => i.Severity == HotkeyIssueSeverity.Warning).Select(i => i.Message).ToList();
        if(warnings.Count > 0 && Host != null && !Host.Confirm(Strings.Format(Strings.SettingsHotkeyWarningConfirm, string.Join(Environment.NewLine + Environment.NewLine, warnings)))) return false;

        TryParseRetention(retentionDaysText, out int retentionDays);
        TryParseDelaySeconds(captureDelaySecondsText, out int delaySeconds);
        AppSettings target = settings.Settings;
        target.StorageFolder = storageFolder.Trim();
        target.FileNamePattern = string.IsNullOrWhiteSpace(fileNamePattern) ? AppSettings.DefaultFileNamePattern : fileNamePattern;
        target.ExportFolder = exportFolder.Trim();
        target.HotkeyCaptureEdit = normalizeHotkey(hotkeyCaptureEdit);
        target.HotkeyCaptureSave = normalizeHotkey(hotkeyCaptureSave);
        target.HotkeyHistory = normalizeHotkey(hotkeyHistory);
        target.HotkeyCaptureDelayed = normalizeHotkey(hotkeyCaptureDelayed);
        target.HotkeyRemotePrintScreen = normalizeHotkey(hotkeyRemotePrintScreen);
        target.CaptureHotkeysInRemoteSessions = captureHotkeysInRemoteSessions;
        target.CaptureDelaySeconds = delaySeconds;
        target.CaptureDelayAskEachTime = captureDelayAskEachTime;
        target.DelayedCaptureAction = delayedCaptureAction;
        target.MagnifierZoom = magnifierZoom;
        target.MagnifierSize = magnifierSize;
        target.ShowMagnifierKeyHints = showMagnifierKeyHints;
        target.DefaultColor = defaultColor;
        target.HighlightColor = highlightColor;
        target.DefaultThickness = defaultThickness;
        target.DefaultFontSize = defaultFontSize;
        target.DefaultBlockSize = defaultBlockSize;
        target.DefaultBlurRadius = defaultBlurRadius;
        target.DefaultStampSize = defaultStampSize;
        target.DefaultCursorKind = defaultCursorKind;
        target.DefaultFillColor = defaultFillColor;
        target.DefaultTextBackground = defaultTextBackground;
        target.DefaultSmooth = defaultSmooth;
        target.CursorVisible = cursorVisible;
        target.Autostart = autostartEnabled;
        target.RetentionDays = retentionDays;
        target.NotifyOnSaveOnly = notifyOnSaveOnly;
        target.CheckForUpdates = checkForUpdates;

        try {
            if(autostart.IsEnabled != autostartEnabled) {
                autostart.SetEnabled(autostartEnabled);
                Log.Info($"Autostart {(autostartEnabled ? "eingeschaltet" : "ausgeschaltet")}");
            }
        } catch(Exception ex) {
            Log.Error(ex, "Autostart konnte nicht geändert werden");
            Host?.ShowError(Strings.Format(Strings.SettingsAutostartFailed, ex.Message));
        }

        settings.Save();
        Log.Info("Einstellungen gespeichert");
        // Werte nach dem Normalisieren übernehmen und Hotkey-Status neu anzeigen
        load(settings.Settings);
        OnPropertyChanged(string.Empty);
        SavedMessage = Strings.SettingsSaved;
        return true;
    }

    /// <summary>Hotkey-Status neu anzeigen (z. B. nach dem Aussetzen während der Aufzeichnung).</summary>
    public void RefreshHotkeyStatus() {
        OnPropertyChanged(nameof(HotkeyCaptureEditStatus));
        OnPropertyChanged(nameof(HotkeyCaptureSaveStatus));
        OnPropertyChanged(nameof(HotkeyHistoryStatus));
        OnPropertyChanged(nameof(HotkeyCaptureDelayedStatus));
        OnPropertyChanged(nameof(HotkeyRemotePrintScreenStatus));
        OnPropertyChanged(nameof(PrintScreenStatus));
    }

    /// <summary>
    /// Ordner prüfen: leer ist erlaubt (Standard); sonst ein vollständiger, gültiger Pfad (Umgebungsvariablen erlaubt).
    /// </summary>
    public static string? ValidateFolder(string? text) {
        if(string.IsNullOrWhiteSpace(text)) return null;
        try {
            string expanded = Environment.ExpandEnvironmentVariables(text.Trim());
            if(expanded.IndexOfAny(Path.GetInvalidPathChars()) >= 0 || expanded.IndexOf('?') >= 0 || expanded.IndexOf('*') >= 0) return Strings.SettingsFolderInvalid;
            if(!Path.IsPathFullyQualified(expanded)) return Strings.SettingsFolderNotAbsolute;
            Path.GetFullPath(expanded);
            return null;
        } catch(Exception ex) {
            Log.Warn($"Ungültiger Ordner „{text}“: {ex.Message}");
            return Strings.SettingsFolderInvalid;
        }
    }

    public static bool TryParseRetention(string? text, out int days) {
        days = 0;
        if(string.IsNullOrWhiteSpace(text)) return true;
        if(!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out days)) return false;
        return days >= 0 && days <= AppSettings.MaxRetentionDays;
    }

    /// <summary>Verzögerung in Sekunden: ganze Zahl im erlaubten Bereich (leer ist ungültig).</summary>
    public static bool TryParseDelaySeconds(string? text, out int seconds) {
        seconds = 0;
        if(string.IsNullOrWhiteSpace(text)) return false;
        if(!int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out seconds)) return false;
        return seconds >= AppSettings.MinCaptureDelaySeconds && seconds <= AppSettings.MaxCaptureDelaySeconds;
    }

    private void load(AppSettings source) {
        storageFolder = source.StorageFolder;
        fileNamePattern = source.FileNamePattern;
        exportFolder = source.ExportFolder;
        retentionDaysText = source.RetentionDays.ToString(CultureInfo.CurrentCulture);
        notifyOnSaveOnly = source.NotifyOnSaveOnly;
        checkForUpdates = source.CheckForUpdates;
        hotkeyCaptureEdit = source.HotkeyCaptureEdit;
        hotkeyCaptureSave = source.HotkeyCaptureSave;
        hotkeyHistory = source.HotkeyHistory;
        hotkeyCaptureDelayed = source.HotkeyCaptureDelayed;
        hotkeyRemotePrintScreen = source.HotkeyRemotePrintScreen;
        captureHotkeysInRemoteSessions = source.CaptureHotkeysInRemoteSessions;
        captureDelaySecondsText = source.CaptureDelaySeconds.ToString(CultureInfo.CurrentCulture);
        captureDelayAskEachTime = source.CaptureDelayAskEachTime;
        delayedCaptureAction = source.DelayedCaptureAction;
        magnifierZoom = source.MagnifierZoom;
        magnifierSize = source.MagnifierSize;
        loadedMagnifierZoom = source.MagnifierZoom;
        loadedMagnifierSize = source.MagnifierSize;
        showMagnifierKeyHints = source.ShowMagnifierKeyHints;
        defaultColor = normalizeColor(source.DefaultColor);
        highlightColor = normalizeColor(source.HighlightColor);
        defaultThickness = source.DefaultThickness;
        defaultFontSize = source.DefaultFontSize;
        defaultBlockSize = source.DefaultBlockSize;
        defaultBlurRadius = source.DefaultBlurRadius;
        defaultStampSize = source.DefaultStampSize;
        defaultCursorKind = source.DefaultCursorKind;
        defaultFillColor = AnnotationColors.TryParse(source.DefaultFillColor, out Color fill) ? AnnotationColors.ToHex(fill) : "";
        defaultTextBackground = source.DefaultTextBackground;
        defaultSmooth = source.DefaultSmooth;
        cursorVisible = source.CursorVisible;
        // Wahrheit für den Autostart ist die Registry
        try {
            autostartEnabled = autostart.IsEnabled;
        } catch(Exception ex) {
            Log.Error(ex, "Autostart-Status nicht lesbar");
            autostartEnabled = source.Autostart;
        }
    }

    private List<(HotkeyAction Action, string Text)> getBindings() {
        return new List<(HotkeyAction Action, string Text)> {
            (HotkeyAction.CaptureEdit, hotkeyCaptureEdit),
            (HotkeyAction.CaptureSave, hotkeyCaptureSave),
            (HotkeyAction.ShowHistory, hotkeyHistory),
            (HotkeyAction.CaptureDelayed, hotkeyCaptureDelayed),
            (HotkeyAction.RemotePrintScreen, hotkeyRemotePrintScreen)
        };
    }

    /// <summary>
    /// Probe-Registrierung nur für geänderte Hotkeys – ein unveränderter (z. B. über den Hook laufender) Hotkey soll nicht
    /// bei jedem Speichern erneut gemeldet werden.
    /// </summary>
    private (HotkeyProbeResult Result, int Error) probe(HotkeyAction action, HotkeyBinding binding) {
        if(hotkeys == null) return (HotkeyProbeResult.Available, 0);
        string current = action switch {
            HotkeyAction.CaptureEdit => settings.Settings.HotkeyCaptureEdit,
            HotkeyAction.CaptureSave => settings.Settings.HotkeyCaptureSave,
            HotkeyAction.CaptureDelayed => settings.Settings.HotkeyCaptureDelayed,
            HotkeyAction.RemotePrintScreen => settings.Settings.HotkeyRemotePrintScreen,
            _ => settings.Settings.HotkeyHistory
        };
        if(HotkeyBinding.TryParse(current, out HotkeyBinding? applied) && applied == binding) return (HotkeyProbeResult.Available, 0);
        HotkeyProbeResult result = hotkeys.Probe(binding, out int error);
        return (result, error);
    }

    private void showHotkeyIssues(List<HotkeyIssue> issues) {
        HasHotkeyErrors = issues.Any(i => i.Severity == HotkeyIssueSeverity.Error);
        HotkeyMessages = string.Join(Environment.NewLine, issues.Select(i => i.Message));
    }

    private string getStatusText(HotkeyAction action) {
        if(hotkeys == null) return Strings.SettingsHotkeyStatusInactive;
        if(!hotkeys.Status.TryGetValue(action, out HotkeyStatus? status)) return Strings.SettingsHotkeyStatusOff;
        switch(status.State) {
            case HotkeyState.Registered: return Strings.SettingsHotkeyStatusRegistered;
            case HotkeyState.Hooked: return Strings.SettingsHotkeyStatusHooked;
            case HotkeyState.Failed: return Strings.Format(Strings.SettingsHotkeyStatusFailed, status.Error);
            case HotkeyState.Invalid: return Strings.SettingsHotkeyStatusInvalid;
            case HotkeyState.Suspended: return Strings.SettingsHotkeyStatusSuspended;
            case HotkeyState.RemoteOnly: return Strings.SettingsHotkeyStatusRemoteOnly;
        }
        return Strings.SettingsHotkeyStatusOff;
    }

    private string getPrintScreenState() {
        if(hotkeys == null) return Strings.SettingsHotkeyStatusInactive;
        List<HotkeyStatus> print = hotkeys.Status.Values.Where(s => s.Binding != null && s.Binding.IsPrintScreen).ToList();
        if(print.Any(s => s.State == HotkeyState.Hooked)) return Strings.PrintStatusHooked;
        if(print.Any(s => s.State == HotkeyState.Registered)) return Strings.PrintStatusRegistered;
        if(print.Any(s => s.State == HotkeyState.Failed)) return Strings.PrintStatusUnavailable;
        if(hotkeys.Status.Values.Any(s => s.State == HotkeyState.Suspended)) return Strings.SettingsHotkeyStatusSuspended;
        return Strings.PrintStatusUnused;
    }

    private static string normalizeHotkey(string text) {
        if(string.IsNullOrWhiteSpace(text)) return "";
        return HotkeyBinding.TryParse(text, out HotkeyBinding? binding) && binding != null ? binding.ToString() : text.Trim();
    }

    private static string normalizeColor(string text) {
        return AnnotationColors.TryParse(text, out Color color) ? AnnotationColors.ToHex(color) : AnnotationColors.Red;
    }

    private static IReadOnlyList<ColorChoice> buildPalette(string current) {
        List<string> colors = AnnotationColors.Palette.Select(normalizeColor).ToList();
        if(!colors.Contains(current, StringComparer.OrdinalIgnoreCase)) colors.Add(current);
        return colors.Select(hex => new ColorChoice(hex, AnnotationColors.GetBrush(hex))).ToList();
    }

    private static IReadOnlyList<ColorChoice> buildFillPalette(string current) {
        List<string> colors = new List<string> { "" };
        colors.AddRange(AnnotationColors.Palette.Select(normalizeColor));
        colors.AddRange(AnnotationColors.Palette.Select(c => AnnotationColors.ToHex(AnnotationColors.WithAlpha(AnnotationColors.Parse(c), EditorViewModel.TranslucentFillAlpha))));
        if(!colors.Contains(current, StringComparer.OrdinalIgnoreCase)) colors.Add(current);
        return colors.Select(hex => new ColorChoice(hex, LineCapIcons.GetFillBrush(hex))).ToList();
    }

    private static IReadOnlyList<T> withValue<T>(IReadOnlyList<T> values, T current) {
        List<T> list = values.ToList();
        if(!list.Contains(current)) {
            list.Add(current);
            list.Sort();
        }
        return list;
    }
}
