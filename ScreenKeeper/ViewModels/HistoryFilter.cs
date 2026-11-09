using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Schnellwahl des Datumsfilters.
/// </summary>
public enum HistoryQuickRange {
    All,
    Today,
    Last7Days,
    Last30Days
}

/// <summary>
/// Filter der History: Zeitraum (Tage <see cref="From"/> bis <see cref="To"/>, jeweils einschließlich, lokale Zeit),
/// Text (ohne Groß-/Kleinschreibung) im Dateinamen, im Programmnamen oder Fenstertitel des Vordergrundprogramms und
/// optional nur permanent markierte Aufnahmen. Null bzw. leer = keine Einschränkung.
/// </summary>
public sealed record HistoryFilter(DateTime? From, DateTime? To, string? Text, bool PermanentOnly = false) {
    public static HistoryFilter All { get; } = new HistoryFilter(null, null, null);

    public bool IsEmpty => !From.HasValue && !To.HasValue && string.IsNullOrWhiteSpace(Text) && !PermanentOnly;

    /// <summary>
    /// Braucht der Filter die Kennzeichen aus der JSON (<see cref="CaptureEntry.Metadata"/>)? Dann lädt sie der
    /// <see cref="HistoryPager"/> vor dem Prüfen (im Hintergrund).
    /// </summary>
    public bool NeedsMetadata => PermanentOnly || !string.IsNullOrWhiteSpace(Text);

    /// <summary>Filter für eine Schnellwahl relativ zu <paramref name="today"/> (Text und „permanent“ bleiben erhalten).</summary>
    public static HistoryFilter ForQuickRange(HistoryQuickRange range, DateTime today, string? text = null, bool permanentOnly = false) {
        DateTime day = today.Date;
        switch(range) {
            case HistoryQuickRange.Today: return new HistoryFilter(day, day, text, permanentOnly);
            case HistoryQuickRange.Last7Days: return new HistoryFilter(day.AddDays(-6), null, text, permanentOnly);
            case HistoryQuickRange.Last30Days: return new HistoryFilter(day.AddDays(-29), null, text, permanentOnly);
        }
        return new HistoryFilter(null, null, text, permanentOnly);
    }

    /// <summary>
    /// Passt der Eintrag? Für Text und „permanent“ zählen die Kennzeichen in <see cref="CaptureEntry.Metadata"/>; sind sie
    /// (noch) nicht geladen, gilt nur der Dateiname bzw. „nicht permanent“.
    /// </summary>
    public bool Matches(CaptureEntry entry) {
        DateTime day = HistoryGrouping.GetDay(entry.Time);
        if(From.HasValue && day < From.Value.Date) return false;
        if(To.HasValue && day > To.Value.Date) return false;
        CaptureMetadata? metadata = entry.Metadata;
        if(PermanentOnly && (metadata == null || !metadata.Permanent)) return false;
        if(!string.IsNullOrWhiteSpace(Text)) {
            string text = Text.Trim();
            if(contains(entry.Name, text)) return true;
            return metadata != null && (contains(metadata.ForegroundProcess, text) || contains(metadata.ForegroundTitle, text));
        }
        return true;
    }

    private static bool contains(string value, string text) {
        return value.IndexOf(text, StringComparison.CurrentCultureIgnoreCase) >= 0;
    }
}
