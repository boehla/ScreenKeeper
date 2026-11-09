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
/// Filter der History: Zeitraum (Tage <see cref="From"/> bis <see cref="To"/>, jeweils einschließlich, lokale Zeit)
/// und Text im Dateinamen (ohne Groß-/Kleinschreibung). Null bzw. leer = keine Einschränkung.
/// </summary>
public sealed record HistoryFilter(DateTime? From, DateTime? To, string? Text) {
    public static HistoryFilter All { get; } = new HistoryFilter(null, null, null);

    public bool IsEmpty => !From.HasValue && !To.HasValue && string.IsNullOrWhiteSpace(Text);

    /// <summary>Filter für eine Schnellwahl relativ zu <paramref name="today"/> (Text bleibt erhalten).</summary>
    public static HistoryFilter ForQuickRange(HistoryQuickRange range, DateTime today, string? text = null) {
        DateTime day = today.Date;
        switch(range) {
            case HistoryQuickRange.Today: return new HistoryFilter(day, day, text);
            case HistoryQuickRange.Last7Days: return new HistoryFilter(day.AddDays(-6), null, text);
            case HistoryQuickRange.Last30Days: return new HistoryFilter(day.AddDays(-29), null, text);
        }
        return new HistoryFilter(null, null, text);
    }

    public bool Matches(CaptureEntry entry) {
        DateTime day = HistoryGrouping.GetDay(entry.Time);
        if(From.HasValue && day < From.Value.Date) return false;
        if(To.HasValue && day > To.Value.Date) return false;
        if(!string.IsNullOrWhiteSpace(Text) && entry.Name.IndexOf(Text.Trim(), StringComparison.CurrentCultureIgnoreCase) < 0) return false;
        return true;
    }
}
