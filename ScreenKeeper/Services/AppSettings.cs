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

    /// <summary>Vergrößerung der Lupe (4–16).</summary>
    public int MagnifierZoom { get; set; } = 8;

    /// <summary>Kantenlänge der Lupe in geräteunabhängigen Pixeln.</summary>
    public int MagnifierSize { get; set; } = 160;

    /// <summary>Standardfarbe der Zeichenwerkzeuge (#AARRGGBB).</summary>
    public string DefaultColor { get; set; } = "#FFE53935";

    /// <summary>Standardfarbe des Textmarkers (#AARRGGBB).</summary>
    public string HighlightColor { get; set; } = "#FFFFEB3B";

    /// <summary>Standard-Strichstärke in Bildpixeln.</summary>
    public double DefaultThickness { get; set; } = 4;

    /// <summary>Zuletzt verwendete Schriftgröße des Text-Werkzeugs (Bildpixel).</summary>
    public double DefaultFontSize { get; set; } = 24;

    /// <summary>Zuletzt verwendete Blockgröße beim Verpixeln (Bildpixel).</summary>
    public int DefaultBlockSize { get; set; } = 10;

    /// <summary>Zuletzt verwendete Stärke beim Weichzeichnen (Radius in Bildpixeln).</summary>
    public int DefaultBlurRadius { get; set; } = 8;

    /// <summary>Rechteck und Ellipse gefüllt zeichnen (Füllung in der Rahmenfarbe).</summary>
    public bool DefaultFill { get; set; }

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

    /// <summary>Aufnahmen älter als X Tage in den Papierkorb verschieben; 0 = aus.</summary>
    public int RetentionDays { get; set; }

    /// <summary>Tray-Benachrichtigung bei „nur speichern“.</summary>
    public bool NotifyOnSaveOnly { get; set; } = true;

    /// <summary>Größe und Position des History-Fensters; null = Standard.</summary>
    public WindowPlacement? HistoryWindow { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

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
        MagnifierZoom = Math.Clamp(MagnifierZoom, MinMagnifierZoom, MaxMagnifierZoom);
        if(MagnifierSize < MinMagnifierSize || MagnifierSize > MaxMagnifierSize) MagnifierSize = defaults.MagnifierSize;
        if(string.IsNullOrWhiteSpace(DefaultColor)) DefaultColor = defaults.DefaultColor;
        if(string.IsNullOrWhiteSpace(HighlightColor)) HighlightColor = defaults.HighlightColor;
        if(DefaultThickness <= 0 || DefaultThickness > 100 || double.IsNaN(DefaultThickness)) DefaultThickness = defaults.DefaultThickness;
        if(DefaultFontSize < 6 || DefaultFontSize > 400 || double.IsNaN(DefaultFontSize)) DefaultFontSize = defaults.DefaultFontSize;
        if(DefaultBlockSize < 2 || DefaultBlockSize > 200) DefaultBlockSize = defaults.DefaultBlockSize;
        if(DefaultBlurRadius < 1 || DefaultBlurRadius > 200) DefaultBlurRadius = defaults.DefaultBlurRadius;
        if(DefaultStampSize < 8 || DefaultStampSize > 400 || double.IsNaN(DefaultStampSize)) DefaultStampSize = defaults.DefaultStampSize;
        if(DefaultCursorKind != CursorKinds.Captured && DefaultCursorKind != CursorKinds.Arrow && DefaultCursorKind != CursorKinds.Hand && DefaultCursorKind != CursorKinds.IBeam) DefaultCursorKind = defaults.DefaultCursorKind;
        if(RetentionDays < 0) RetentionDays = 0;
        if(RetentionDays > MaxRetentionDays) RetentionDays = MaxRetentionDays;
        if(HistoryWindow != null && !HistoryWindow.IsValid) HistoryWindow = null;
    }
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
