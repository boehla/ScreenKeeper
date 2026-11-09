using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Inkrementelles Laden der History (UI-frei): liest die lazy Aufzählung der Aufnahmen blockweise und filtert dabei.
/// Nicht threadsicher – es darf immer nur ein <see cref="NextBlock"/> gleichzeitig laufen (typisch im Hintergrund-Task).
/// </summary>
public sealed class HistoryPager {
    private readonly IEnumerator<CaptureEntry> source;
    private readonly HistoryFilter filter;

    public HistoryPager(IEnumerable<CaptureEntry> entries, HistoryFilter filter) {
        source = entries.GetEnumerator();
        this.filter = filter;
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
        while(!IsComplete && block.Count < size) {
            if(!source.MoveNext()) {
                IsComplete = true;
                source.Dispose();
                break;
            }
            Scanned++;
            if(filter.Matches(source.Current)) block.Add(source.Current);
        }
        Returned += block.Count;
        return block;
    }
}
