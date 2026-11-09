using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Inkrementelles Laden der History (UI-frei): liest die lazy Aufzählung der Aufnahmen blockweise und filtert dabei.
/// Braucht der Filter die Kennzeichen aus der JSON (Text, „permanent“), werden sie über <c>loadMetadata</c> geladen –
/// je Eintrag einmal, im Thread von <see cref="NextBlock"/>.
/// Nicht threadsicher – es darf immer nur ein <see cref="NextBlock"/> gleichzeitig laufen (typisch im Hintergrund-Task).
/// </summary>
public sealed class HistoryPager {
    private readonly IEnumerator<CaptureEntry> source;
    private readonly HistoryFilter filter;
    private readonly HistoryFilter dateFilter;
    private readonly Func<CaptureEntry, CaptureMetadata>? loadMetadata;

    public HistoryPager(IEnumerable<CaptureEntry> entries, HistoryFilter filter, Func<CaptureEntry, CaptureMetadata>? loadMetadata = null) {
        source = entries.GetEnumerator();
        this.filter = filter;
        dateFilter = new HistoryFilter(filter.From, filter.To, null);
        this.loadMetadata = loadMetadata;
    }

    public HistoryFilter Filter => filter;

    /// <summary>Alle Einträge gelesen.</summary>
    public bool IsComplete { get; private set; }

    /// <summary>Gelesene Einträge (auch die vom Filter verworfenen).</summary>
    public int Scanned { get; private set; }

    /// <summary>Gelieferte (passende) Einträge.</summary>
    public int Returned { get; private set; }

    /// <summary>
    /// Die nächsten bis zu <paramref name="size"/> passenden Einträge; weniger nur am Ende.
    /// </summary>
    public IReadOnlyList<CaptureEntry> NextBlock(int size) {
        List<CaptureEntry> block = new List<CaptureEntry>(size);
        bool needsMetadata = filter.NeedsMetadata && loadMetadata != null;
        while(!IsComplete && block.Count < size) {
            if(!source.MoveNext()) {
                IsComplete = true;
                source.Dispose();
                break;
            }
            Scanned++;
            CaptureEntry entry = source.Current;
            bool matches = filter.Matches(entry);
            // JSON nur lesen, wenn das Datum passt und der Dateiname allein nicht reicht
            if(!matches && needsMetadata && entry.Metadata == null && dateFilter.Matches(entry)) {
                loadMetadata!(entry);
                matches = filter.Matches(entry);
            }
            if(matches) block.Add(entry);
        }
        Returned += block.Count;
        return block;
    }
}
