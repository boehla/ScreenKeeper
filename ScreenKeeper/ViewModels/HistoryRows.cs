using System.Globalization;
using System.Windows.Media;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Eine Zeile der History-Liste: Tagesüberschrift oder Aufnahme.
/// </summary>
public abstract class HistoryRow : ViewModelBase {
    public abstract bool IsHeader { get; }
}

/// <summary>
/// Überschrift einer Tagesgruppe („Heute“, „Gestern“, Datum) mit Anzahl der geladenen Aufnahmen.
/// </summary>
public sealed class HistoryHeaderRow : HistoryRow {
    private string label;
    private int count;

    public HistoryHeaderRow(DateTime day, string label) {
        Day = day.Date;
        this.label = label;
    }

    public override bool IsHeader => true;

    public DateTime Day { get; }

    public string Label {
        get => label;
        set => SetProperty(ref label, value);
    }

    public int Count {
        get => count;
        set {
            if(SetProperty(ref count, value)) OnPropertyChanged(nameof(CountText));
        }
    }

    public string CountText => Strings.Format(Strings.HistoryGroupCount, count);
}

/// <summary>
/// Liefert Vorschaubilder nach (lazy, im Hintergrund).
/// </summary>
public interface IThumbnailSource {
    void Request(HistoryItem item, int version);
}

/// <summary>
/// Eine Aufnahme in der History. Vorschaubild und Kennzeichen „bearbeitet“ werden erst geladen, wenn die Zeile
/// angezeigt wird (erster Zugriff auf <see cref="Thumbnail"/>).
/// </summary>
public sealed class HistoryItem : HistoryRow {
    private readonly IThumbnailSource? thumbnails;
    private CaptureEntry entry;
    private ImageSource? thumbnail;
    private bool thumbnailRequested;
    private bool isEdited;
    private bool isBroken;
    private int version;

    public HistoryItem(CaptureEntry entry, IThumbnailSource? thumbnails) {
        this.entry = entry;
        this.thumbnails = thumbnails;
    }

    public override bool IsHeader => false;

    public CaptureEntry Entry => entry;

    public CaptureFiles Files => entry.Files;

    public string Name => entry.Name;

    public DateTime Time => entry.Time;

    public string TimeText => entry.Time.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    public string SizeText => FormatSize(entry.Size);

    public bool HasJson => entry.HasJson;

    public string ToolTipText => Strings.Format(Strings.HistoryItemTooltip, Path.GetFileName(entry.Files.PngPath), SizeText, Path.GetDirectoryName(entry.Files.PngPath));

    /// <summary>Lädt beim ersten Zugriff im Hintergrund nach (null bis dahin).</summary>
    public ImageSource? Thumbnail {
        get {
            if(thumbnail == null && !thumbnailRequested && thumbnails != null) {
                thumbnailRequested = true;
                thumbnails.Request(this, version);
            }
            return thumbnail;
        }
    }

    public bool IsThumbnailLoaded => thumbnail != null;

    /// <summary>Bereich oder Zeichnungen vorhanden (aus der JSON, nachgeladen).</summary>
    public bool IsEdited => isEdited;

    /// <summary>JSON vorhanden, aber nicht lesbar.</summary>
    public bool IsBroken => isBroken;

    /// <summary>Kein JSON: beim ersten Bearbeiten wird eines angelegt.</summary>
    public bool IsImageOnly => !entry.HasJson;

    public int Version => version;

    public static string FormatSize(long bytes) {
        if(bytes >= 1024L * 1024) return Strings.Format(Strings.SizeMegabytes, (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.CurrentCulture));
        return Strings.Format(Strings.SizeKilobytes, Math.Max(1, (bytes + 1023) / 1024).ToString(CultureInfo.CurrentCulture));
    }

    /// <summary>Ergebnis des Nachladens übernehmen (im UI-Thread); veraltete Ergebnisse werden ignoriert.</summary>
    public bool SetLoaded(int loadedVersion, ImageSource? image, bool edited, bool broken) {
        if(loadedVersion != version) return false;
        thumbnail = image;
        isEdited = edited;
        isBroken = broken;
        OnPropertyChanged(nameof(Thumbnail));
        OnPropertyChanged(nameof(IsEdited));
        OnPropertyChanged(nameof(IsBroken));
        return true;
    }

    /// <summary>Anfrage verworfen (Warteschlange voll): beim nächsten Anzeigen erneut anfragen.</summary>
    public void CancelRequest(int requestVersion) {
        if(requestVersion == version && thumbnail == null) thumbnailRequested = false;
    }

    /// <summary>
    /// Vorschaubild aus dem Speicher werfen (Cache voll). Ohne Änderungsmeldung – angezeigte Zeilen behalten ihr Bild,
    /// bis sie neu gebunden werden; dann wird es erneut geladen.
    /// </summary>
    public void ReleaseThumbnail() {
        thumbnail = null;
        thumbnailRequested = false;
    }

    /// <summary>
    /// Dateien haben sich geändert (z. B. nach dem Bearbeiten): Eintrag ersetzen und Vorschaubild neu laden.
    /// </summary>
    public void Reload(CaptureEntry updated) {
        entry = updated;
        version++;
        thumbnail = null;
        thumbnailRequested = false;
        OnPropertyChanged(nameof(Entry));
        OnPropertyChanged(nameof(HasJson));
        OnPropertyChanged(nameof(IsImageOnly));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(ToolTipText));
        OnPropertyChanged(nameof(Thumbnail));
    }
}
