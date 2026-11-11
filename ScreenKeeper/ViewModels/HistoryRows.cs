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
/// Eine Aufnahme in der History. Vorschaubild und Kennzeichen aus der JSON (bearbeitet, permanent, Programm im
/// Vordergrund) werden erst geladen, wenn die Zeile angezeigt wird (erster Zugriff auf <see cref="Thumbnail"/>) – es sei
/// denn, der Filter hat sie schon gebraucht (<see cref="CaptureEntry.Metadata"/>).
/// </summary>
public sealed class HistoryItem : HistoryRow {
    /// <summary>Höchstlänge des Fenstertitels in der Zeile (der Tooltip zeigt ihn ganz).</summary>
    public const int MaxTitleLength = 60;

    private readonly IThumbnailSource? thumbnails;
    private CaptureEntry entry;
    private ImageSource? thumbnail;
    private bool thumbnailRequested;
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

    /// <summary>Kennzeichen aus der JSON; null = noch nicht geladen.</summary>
    public CaptureMetadata? Metadata => entry.Metadata;

    /// <summary>Bereich oder Zeichnungen vorhanden (aus der JSON, nachgeladen).</summary>
    public bool IsEdited => entry.Metadata?.IsEdited ?? false;

    /// <summary>JSON vorhanden, aber nicht lesbar.</summary>
    public bool IsBroken => entry.Metadata?.IsBroken ?? false;

    /// <summary>Permanent markiert (wird beim Aufräumen nie gelöscht).</summary>
    public bool IsPermanent => entry.Metadata?.Permanent ?? false;

    public bool HasForeground => entry.Metadata?.HasForeground ?? false;

    /// <summary>Programm im Vordergrund bei der Aufnahme, z. B. „chrome · Neuer Tab“ (Titel gekürzt).</summary>
    public string ForegroundText => FormatForeground(entry.Metadata?.ForegroundProcess ?? "", entry.Metadata?.ForegroundTitle ?? "", MaxTitleLength);

    /// <summary>Programm und vollständiger Fenstertitel (Tooltip).</summary>
    public string ForegroundToolTip => HasForeground ? Strings.Format(Strings.HistoryForegroundTooltip, entry.Metadata!.ForegroundProcess, entry.Metadata.ForegroundTitle) : "";

    public bool HasComment => entry.Metadata?.HasComment ?? false;

    /// <summary>Kommentar einzeilig und gekürzt (Zeile unter dem Programm).</summary>
    public string CommentText => ViewModels.CommentText.SingleLine(entry.Metadata?.Comment, ViewModels.CommentText.LineLength);

    /// <summary>Kommentar mit Zeilenumbrüchen (Tooltip, bei sehr langen Texten gekürzt).</summary>
    public string CommentToolTip => HasComment ? Strings.Format(Strings.HistoryCommentTooltip, ViewModels.CommentText.Preview(entry.Metadata!.Comment, ViewModels.CommentText.ToolTipLength)) : "";

    /// <summary>Kein JSON: beim ersten Bearbeiten wird eines angelegt.</summary>
    public bool IsImageOnly => !entry.HasJson;

    public int Version => version;

    public static string FormatSize(long bytes) {
        if(bytes >= 1024L * 1024) return Strings.Format(Strings.SizeMegabytes, (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.CurrentCulture));
        return Strings.Format(Strings.SizeKilobytes, Math.Max(1, (bytes + 1023) / 1024).ToString(CultureInfo.CurrentCulture));
    }

    /// <summary>„Prozess · Titel“; der Titel wird auf <paramref name="maxTitleLength"/> Zeichen gekürzt (…).</summary>
    public static string FormatForeground(string process, string title, int maxTitleLength) {
        string shortTitle = title.Trim();
        if(shortTitle.Length > maxTitleLength) shortTitle = shortTitle.Substring(0, Math.Max(1, maxTitleLength - 1)).TrimEnd() + "…";
        if(process.Length == 0) return shortTitle;
        if(shortTitle.Length == 0) return process;
        return $"{process} · {shortTitle}";
    }

    /// <summary>
    /// Ergebnis des Nachladens übernehmen (im UI-Thread); veraltete Ergebnisse werden ignoriert. Die Kennzeichen hat der
    /// Lader bereits in <see cref="CaptureEntry.Metadata"/> abgelegt.
    /// </summary>
    public bool SetLoaded(int loadedVersion, ImageSource? image) {
        if(loadedVersion != version) return false;
        thumbnail = image;
        OnPropertyChanged(nameof(Thumbnail));
        notifyMetadata();
        return true;
    }

    /// <summary>
    /// Nur die Kennzeichen haben sich geändert (z. B. „permanent“ umgeschaltet): Eintrag ersetzen, Vorschaubild behalten.
    /// </summary>
    public void UpdateEntry(CaptureEntry updated) {
        entry = updated;
        OnPropertyChanged(nameof(Entry));
        OnPropertyChanged(nameof(HasJson));
        OnPropertyChanged(nameof(IsImageOnly));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(ToolTipText));
        notifyMetadata();
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
        notifyMetadata();
    }

    private void notifyMetadata() {
        OnPropertyChanged(nameof(Metadata));
        OnPropertyChanged(nameof(IsEdited));
        OnPropertyChanged(nameof(IsBroken));
        OnPropertyChanged(nameof(IsPermanent));
        OnPropertyChanged(nameof(HasForeground));
        OnPropertyChanged(nameof(ForegroundText));
        OnPropertyChanged(nameof(ForegroundToolTip));
        OnPropertyChanged(nameof(HasComment));
        OnPropertyChanged(nameof(CommentText));
        OnPropertyChanged(nameof(CommentToolTip));
    }
}
