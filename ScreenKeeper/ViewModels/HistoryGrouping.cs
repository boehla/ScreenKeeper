using System.Globalization;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Eine Tagesgruppe der History.
/// </summary>
public sealed record HistoryGroup(DateTime Day, string Label, IReadOnlyList<CaptureEntry> Entries);

/// <summary>
/// Gruppierung der History nach Tagen (lokale Zeit): Überschrift „Heute“, „Gestern“, sonst Datum lang.
/// </summary>
public static class HistoryGrouping {
    /// <summary>Kultur für Wochentag und Monatsnamen (Oberfläche ist Deutsch).</summary>
    public static CultureInfo DateCulture { get; } = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Tag (lokal) eines Zeitpunkts; UTC-Zeiten werden vorher in lokale Zeit umgerechnet.</summary>
    public static DateTime GetDay(DateTime time) {
        DateTime local = time.Kind == DateTimeKind.Utc ? time.ToLocalTime() : time;
        return local.Date;
    }

    /// <summary>Tag (lokal) eines Zeitpunkts mit Offset (z. B. <c>capturedAt</c> aus der JSON).</summary>
    public static DateTime GetDay(DateTimeOffset time) {
        return time.LocalDateTime.Date;
    }

    public static string GetLabel(DateTime day, DateTime today) {
        DateTime date = day.Date;
        if(date == today.Date) return Strings.HistoryToday;
        if(date == today.Date.AddDays(-1)) return Strings.HistoryYesterday;
        return date.ToString(Strings.HistoryDateFormat, DateCulture);
    }

    /// <summary>
    /// Fasst aufeinanderfolgende Einträge desselben Tages zusammen (Eingabe neueste zuerst).
    /// </summary>
    public static List<HistoryGroup> Group(IEnumerable<CaptureEntry> entries, DateTime today) {
        List<HistoryGroup> groups = new List<HistoryGroup>();
        List<CaptureEntry>? current = null;
        DateTime currentDay = DateTime.MinValue;
        foreach(CaptureEntry entry in entries) {
            DateTime day = GetDay(entry.Time);
            if(current == null || day != currentDay) {
                current = new List<CaptureEntry>();
                currentDay = day;
                groups.Add(new HistoryGroup(day, GetLabel(day, today), current));
            }
            current.Add(entry);
        }
        return groups;
    }
}
