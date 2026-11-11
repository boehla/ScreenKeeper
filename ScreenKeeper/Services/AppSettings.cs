using System.Text.Json;
using System.Text.Json.Serialization;
using ScreenKeeper.Models;

namespace ScreenKeeper.Services;

/// <summary>
/// Benutzereinstellungen (<c>%APPDATA%\ScreenKeeper\settings.json</c>). Fehlende Werte bekommen die Standardwerte
/// aus den Initialisierern, unbekannte Felder bleiben über <see cref="ExtensionData"/> erhalten.
/// </summary>
public sealed class AppSettings {
    public const string DefaultFileNamePattern = "{yyyy}-{MM}-{dd}_{HH}-{mm}-{ss}";
    public const int MinMagnifierZoom = 4;
    public const int MaxMagnifierZoom = 16;
    public const int MinMagnifierSize = 64;
    public const int MaxMagnifierSize = 600;
    public const int MaxRetentionDays = 36500;
    public const int DefaultRetentionDays = 30;

    /// <summary>Ablageordner; leer = Bilder\ScreenKeeper.</summary>
    public string StorageFolder { get; set; } = "";

    /// <summary>Dateinamen-Muster mit Platzhaltern {yyyy} {MM} {dd} {HH} {mm} {ss}.</summary>
    public string FileNamePattern { get; set; } = DefaultFileNamePattern;

    /// <summary>Exportordner; leer = &lt;Ablageordner&gt;\Export.</summary>
    public string ExportFolder { get; set; } = "";

    /// <summary>Hotkey: Aufnahme + Bearbeiten.</summary>
    public string HotkeyCaptureEdit { get; set; } = "PrintScreen";

    /// <summary>Hotkey: Aufnahme nur speichern.</summary>
    public string HotkeyCaptureSave { get; set; } = "Ctrl+PrintScreen";

    /// <summary>Hotkey: History öffnen.</summary>
    public string HotkeyHistory { get; set; } = "Shift+PrintScreen";

    /// <summary>Hotkey: verzögerte Aufnahme (Countdown); leer = aus.</summary>
    public string HotkeyCaptureDelayed { get; set; } = "";

    /// <summary>
    /// Hotkey: im Remotedesktop ein reines Druck an die Sitzung durchreichen; leer = aus. Wirkt nur, solange ein
    /// Remote-Fenster im Vordergrund ist und <see cref="CaptureHotkeysInRemoteSessions"/> an ist.
    /// </summary>
    public string HotkeyRemotePrintScreen { get; set; } = "";

    /// <summary>Hotkeys auch bei aktivem Remotedesktop-Fenster (z. B. mstsc im Vollbild) abfangen.</summary>
    public bool CaptureHotkeysInRemoteSessions { get; set; } = true;

    /// <summary>Fensterklassen von Remotedesktop-Fenstern, durch <c>;</c> oder <c>,</c> getrennt (nur in der Datei einstellbar).</summary>
    public string RemoteSessionWindowClasses { get; set; } = RemoteSession.DefaultWindowClasses;

    public const int MinCaptureDelaySeconds = 1;
    public const int MaxCaptureDelaySeconds = 60;

    /// <summary>Verzögerung der verzögerten Aufnahme in Sekunden (1–60); bei „jedes Mal fragen“ der Vorschlag.</summary>
    public int CaptureDelaySeconds { get; set; } = 3;

    /// <summary>Bei jeder verzögerten Aufnahme nach den Sekunden fragen.</summary>
    public bool CaptureDelayAskEachTime { get; set; }

    /// <summary>Was nach dem Countdown passiert (siehe <see cref="DelayedCaptureActions"/>).</summary>
    public string DelayedCaptureAction { get; set; } = DelayedCaptureActions.Edit;

    /// <summary>Vergrößerung der Lupe (4–16).</summary>
    public int MagnifierZoom { get; set; } = 8;

    /// <summary>Kantenlänge der Lupe in geräteunabhängigen Pixeln.</summary>
    public int MagnifierSize { get; set; } = 160;

    /// <summary>Unter der Lupe eine Zeile mit der Tastenbelegung (Rad: Zoom, +/-: Größe) anzeigen.</summary>
    public bool ShowMagnifierKeyHints { get; set; } = true;

    /// <summary>
    /// Zuletzt im Editor gewählter Bereich in Bildschirmkoordinaten des virtuellen Desktops (physische Pixel; nicht
    /// in Bildpixeln, damit er auf der nächsten Aufnahme an derselben Stelle liegt). Null = keiner.
    /// </summary>
    public PixelRect? LastSelection { get; set; }

    /// <summary>Standardfarbe der Zeichenwerkzeuge (#AARRGGBB).</summary>
    public string DefaultColor { get; set; } = "#FFE53935";

    /// <summary>Standardfarbe des Textmarkers (#AARRGGBB).</summary>
    public string HighlightColor { get; set; } = "#FFFFEB3B";

    /// <summary>Standard-Strichstärke in Bildpixeln.</summary>
    public double DefaultThickness { get; set; } = 4;

    /// <summary>Zuletzt verwendete Schriftgröße des Text-Werkzeugs (Bildpixel).</summary>
    public double DefaultFontSize { get; set; } = 24;

    /// <summary>Zuletzt gewählt: neue Textfelder mit automatischer Schriftgröße.</summary>
    public bool DefaultTextAutoFontSize { get; set; } = true;

    /// <summary>Zuletzt verwendete Ausrichtung des Text-Werkzeugs (siehe <see cref="TextAlignments"/>).</summary>
    public string DefaultTextAlignment { get; set; } = TextAlignments.TopLeft;

    /// <summary>Zuletzt verwendete Blockgröße beim Verpixeln (Bildpixel).</summary>
    public int DefaultBlockSize { get; set; } = 10;

    /// <summary>Zuletzt verwendete Stärke beim Weichzeichnen (Radius in Bildpixeln).</summary>
    public int DefaultBlurRadius { get; set; } = 8;

    /// <summary>
    /// Altes Feld (bis 0.6): Rechteck und Ellipse in der Rahmenfarbe füllen. Wird nur noch gelesen und von
    /// <see cref="Normalize"/> nach <see cref="DefaultFillColor"/> übernommen (true → Standardfarbe als Füllung).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? DefaultFill { get; set; }

    /// <summary>Zuletzt verwendete Füllfarbe für Rechteck und Ellipse (#AARRGGBB, Transparenz erlaubt); leer = keine Füllung.</summary>
    public string DefaultFillColor { get; set; } = "";

    /// <summary>Zuletzt verwendetes Symbol am Anfang einer Linie (siehe <see cref="LineCaps"/>).</summary>
    public string DefaultLineStartCap { get; set; } = LineAnnotation.DefaultStartCap;

    /// <summary>Zuletzt verwendetes Symbol am Ende einer Linie.</summary>
    public string DefaultLineEndCap { get; set; } = LineAnnotation.DefaultEndCap;

    /// <summary>Zuletzt verwendetes Symbol am Anfang eines Pfeils.</summary>
    public string DefaultArrowStartCap { get; set; } = ArrowAnnotation.DefaultStartCap;

    /// <summary>Zuletzt verwendetes Symbol an der Spitze eines Pfeils.</summary>
    public string DefaultArrowEndCap { get; set; } = ArrowAnnotation.DefaultEndCap;

    /// <summary>Zuletzt verwendetes Symbol am Anfang einer Freihand-Linie.</summary>
    public string DefaultFreehandStartCap { get; set; } = FreehandAnnotation.DefaultStartCap;

    /// <summary>Zuletzt verwendetes Symbol am Ende einer Freihand-Linie.</summary>
    public string DefaultFreehandEndCap { get; set; } = FreehandAnnotation.DefaultEndCap;

    /// <summary>Text mit Hintergrund zeichnen.</summary>
    public bool DefaultTextBackground { get; set; }

    /// <summary>Freihand-Linien glätten.</summary>
    public bool DefaultSmooth { get; set; } = true;

    /// <summary>Zuletzt verwendeter Durchmesser des Nummern-Stempels (Bildpixel).</summary>
    public double DefaultStampSize { get; set; } = 32;

    /// <summary>Zuletzt verwendete Zeigerart der virtuellen Maus (captured, arrow, hand, ibeam).</summary>
    public string DefaultCursorKind { get; set; } = CursorKinds.Arrow;

    /// <summary>Aufgenommenen Mauszeiger standardmäßig anzeigen.</summary>
    public bool CursorVisible { get; set; } = true;

    /// <summary>Mit Windows starten (Wahrheit liegt in der Registry, siehe AutostartService).</summary>
    public bool Autostart { get; set; }

    /// <summary>
    /// Aufnahmen älter als X Tage in den Papierkorb verschieben (außer permanent markierte); 0 = aus.
    /// Fehlt das Feld in der Datei, gilt der Standard <see cref="DefaultRetentionDays"/>.
    /// </summary>
    public int RetentionDays { get; set; } = DefaultRetentionDays;

    /// <summary>Stand des Dateiformats dieser Einstellungen; aktuell <see cref="CurrentSettingsVersion"/>.</summary>
    public const int CurrentSettingsVersion = 1;

    /// <summary>
    /// Version des Einstellungsformats. Fehlt das Feld (Dateien bis 0.6.x), hat <see cref="Normalize"/> einmalige
    /// Umstellungen vorzunehmen (z. B. Aufbewahrung 0 → 30 Tage, weil 0 damals der Standard „aus“ war).
    /// </summary>
    public int? SettingsVersion { get; set; }

    /// <summary>Einmal täglich im Hintergrund nach einer neuen Version suchen (GitHub-Releases).</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Tray-Benachrichtigung bei „nur speichern“.</summary>
    public bool NotifyOnSaveOnly { get; set; } = true;

    /// <summary>Größe und Position des History-Fensters; null = Standard.</summary>
    public WindowPlacement? HistoryWindow { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    /// <summary>
    /// Alle Werte, die die Hotkey-Registrierung beeinflussen – ändert sich der Text, müssen die Hotkeys neu angewendet werden.
    /// </summary>
    public string GetHotkeySignature() {
        return string.Join("|", HotkeyCaptureEdit, HotkeyCaptureSave, HotkeyHistory, HotkeyCaptureDelayed, HotkeyRemotePrintScreen,
            CaptureHotkeysInRemoteSessions ? "remote" : "local", RemoteSessionWindowClasses);
    }

    /// <summary>
    /// Ersetzt ungültige oder fehlende (null) Werte durch Standardwerte.
    /// </summary>
    public void Normalize() {
        AppSettings defaults = new AppSettings();
        StorageFolder ??= "";
        ExportFolder ??= "";
        if(string.IsNullOrWhiteSpace(FileNamePattern)) FileNamePattern = defaults.FileNamePattern;
        HotkeyCaptureEdit ??= defaults.HotkeyCaptureEdit;
        HotkeyCaptureSave ??= defaults.HotkeyCaptureSave;
        HotkeyHistory ??= defaults.HotkeyHistory;
        HotkeyCaptureDelayed ??= "";
        HotkeyRemotePrintScreen ??= "";
        if(string.IsNullOrWhiteSpace(RemoteSessionWindowClasses)) RemoteSessionWindowClasses = defaults.RemoteSessionWindowClasses;
        CaptureDelaySeconds = Math.Clamp(CaptureDelaySeconds, MinCaptureDelaySeconds, MaxCaptureDelaySeconds);
        if(DelayedCaptureAction != DelayedCaptureActions.Edit && DelayedCaptureAction != DelayedCaptureActions.Save) DelayedCaptureAction = defaults.DelayedCaptureAction;
        MagnifierZoom = Math.Clamp(MagnifierZoom, MinMagnifierZoom, MaxMagnifierZoom);
        if(MagnifierSize < MinMagnifierSize || MagnifierSize > MaxMagnifierSize) MagnifierSize = defaults.MagnifierSize;
        if(string.IsNullOrWhiteSpace(DefaultColor)) DefaultColor = defaults.DefaultColor;
        if(string.IsNullOrWhiteSpace(HighlightColor)) HighlightColor = defaults.HighlightColor;
        if(DefaultThickness <= 0 || DefaultThickness > 100 || double.IsNaN(DefaultThickness)) DefaultThickness = defaults.DefaultThickness;
        if(DefaultFontSize < 6 || DefaultFontSize > 400 || double.IsNaN(DefaultFontSize)) DefaultFontSize = defaults.DefaultFontSize;
        DefaultTextAlignment = TextAlignments.Normalize(DefaultTextAlignment, defaults.DefaultTextAlignment);
        if(DefaultBlockSize < 2 || DefaultBlockSize > 200) DefaultBlockSize = defaults.DefaultBlockSize;
        if(DefaultBlurRadius < 1 || DefaultBlurRadius > 200) DefaultBlurRadius = defaults.DefaultBlurRadius;
        if(DefaultStampSize < 8 || DefaultStampSize > 400 || double.IsNaN(DefaultStampSize)) DefaultStampSize = defaults.DefaultStampSize;
        if(DefaultCursorKind != CursorKinds.Captured && DefaultCursorKind != CursorKinds.Arrow && DefaultCursorKind != CursorKinds.Hand && DefaultCursorKind != CursorKinds.IBeam) DefaultCursorKind = defaults.DefaultCursorKind;
        // Füllung: altes bool-Feld übernehmen (nur wenn noch keine Füllfarbe gesetzt ist), ungültige Farben → keine Füllung
        DefaultFillColor ??= "";
        if(DefaultFill == true && DefaultFillColor.Length == 0) DefaultFillColor = DefaultColor;
        DefaultFill = null;
        if(DefaultFillColor.Length > 0 && !AnnotationColors.TryParse(DefaultFillColor, out _)) DefaultFillColor = "";
        DefaultLineStartCap = LineCaps.Normalize(DefaultLineStartCap, defaults.DefaultLineStartCap);
        DefaultLineEndCap = LineCaps.Normalize(DefaultLineEndCap, defaults.DefaultLineEndCap);
        DefaultArrowStartCap = LineCaps.Normalize(DefaultArrowStartCap, defaults.DefaultArrowStartCap);
        DefaultArrowEndCap = LineCaps.Normalize(DefaultArrowEndCap, defaults.DefaultArrowEndCap);
        DefaultFreehandStartCap = LineCaps.Normalize(DefaultFreehandStartCap, defaults.DefaultFreehandStartCap);
        DefaultFreehandEndCap = LineCaps.Normalize(DefaultFreehandEndCap, defaults.DefaultFreehandEndCap);
        if(RetentionDays < 0) RetentionDays = 0;
        // Einmalige Umstellung alter Dateien (ohne settingsVersion): Aufbewahrung war standardmäßig aus (0) und wurde
        // immer geschrieben – ab 0.7.0 gilt 30 Tage als Standard, bewusst gesetzte andere Werte bleiben erhalten
        if((SettingsVersion ?? 0) < 1 && RetentionDays == 0) RetentionDays = DefaultRetentionDays;
        SettingsVersion = CurrentSettingsVersion;
        if(RetentionDays > MaxRetentionDays) RetentionDays = MaxRetentionDays;
        if(HistoryWindow != null && !HistoryWindow.IsValid) HistoryWindow = null;
        if(LastSelection.HasValue && !IsValidLastSelection(LastSelection.Value)) LastSelection = null;
    }

    /// <summary>Größte erlaubte Kantenlänge eines gemerkten Bereichs (Schutz vor unsinnigen Werten in der Datei).</summary>
    public const int MaxLastSelectionSize = 100000;

    /// <summary>Gemerkter Bereich brauchbar? Breite und Höhe mindestens 1 und nicht unsinnig groß.</summary>
    public static bool IsValidLastSelection(PixelRect region) {
        return region.Width > 0 && region.Height > 0 && region.Width <= MaxLastSelectionSize && region.Height <= MaxLastSelectionSize
            && Math.Abs((long)region.X) <= MaxLastSelectionSize && Math.Abs((long)region.Y) <= MaxLastSelectionSize;
    }
}

/// <summary>
/// Werte für <see cref="AppSettings.DelayedCaptureAction"/>.
/// </summary>
public static class DelayedCaptureActions {
    /// <summary>Aufnahme + Editor.</summary>
    public const string Edit = "edit";

    /// <summary>Aufnahme nur speichern.</summary>
    public const string Save = "save";
}

/// <summary>
/// Gemerkte Lage eines Fensters in geräteunabhängigen Pixeln (normale, nicht maximierte Lage).
/// </summary>
public sealed class WindowPlacement {
    public double Left { get; set; }

    public double Top { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    public bool Maximized { get; set; }

    [JsonIgnore]
    public bool IsValid => Width >= 200 && Height >= 150 && Width < 100000 && Height < 100000
        && !double.IsNaN(Left) && !double.IsNaN(Top) && !double.IsInfinity(Left) && !double.IsInfinity(Top);
}
