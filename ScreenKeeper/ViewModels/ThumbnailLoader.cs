using System.Windows.Media;
using System.Windows.Threading;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Lädt Vorschaubilder der History im Hintergrund: zuletzt angefragte zuerst (sichtbare Zeilen beim Scrollen),
/// höchstens zwei gleichzeitig, beim Dekodieren verkleinert und eingefroren. Fehlt das Vorschaubild, wird es aus dem PNG
/// erzeugt. Die Zahl der Bilder im Speicher ist begrenzt (die am längsten nicht angefragten fliegen zuerst raus).
/// </summary>
public sealed class ThumbnailLoader : IThumbnailSource {
    /// <summary>Breite beim Dekodieren (Anzeige 192 px, Reserve für 125 % Skalierung).</summary>
    public const int DecodeWidth = 240;

    private const int maxWorkers = 2;
    private const int maxPending = 120;

    private readonly IStorageService storage;
    private readonly Dispatcher dispatcher;
    private readonly int capacity;
    private readonly object sync = new object();
    private readonly LinkedList<(HistoryItem Item, int Version)> pending = new LinkedList<(HistoryItem Item, int Version)>();
    private readonly LinkedList<HistoryItem> cached = new LinkedList<HistoryItem>();
    private readonly Dictionary<HistoryItem, LinkedListNode<HistoryItem>> cachedNodes = new Dictionary<HistoryItem, LinkedListNode<HistoryItem>>();
    private int running;
    private int loadedCount;

    public ThumbnailLoader(IStorageService storage, Dispatcher dispatcher, int capacity = 300) {
        this.storage = storage;
        this.dispatcher = dispatcher;
        this.capacity = capacity;
    }

    /// <summary>Insgesamt geladene Vorschaubilder (Diagnose).</summary>
    public int LoadedCount => loadedCount;

    /// <summary>Vorschaubilder derzeit im Speicher.</summary>
    public int CachedCount => cached.Count;

    /// <summary>Im UI-Thread (aus dem Getter von <see cref="HistoryItem.Thumbnail"/>).</summary>
    public void Request(HistoryItem item, int version) {
        (HistoryItem Item, int Version)? dropped = null;
        lock(sync) {
            pending.AddLast((item, version));
            if(pending.Count > maxPending) {
                dropped = pending.First!.Value;
                pending.RemoveFirst();
            }
            if(running < maxWorkers) {
                running++;
                Task.Run(work);
            }
        }
        if(dropped.HasValue) dropped.Value.Item.CancelRequest(dropped.Value.Version);
    }

    /// <summary>Alle Bilder aus dem Speicher werfen und Warteschlange leeren (z. B. beim Neuladen der Liste).</summary>
    public void Clear() {
        lock(sync) {
            pending.Clear();
        }
        foreach(HistoryItem item in cached) item.ReleaseThumbnail();
        cached.Clear();
        cachedNodes.Clear();
    }

    /// <summary>Eintrag aus dem Cache entfernen (gelöscht oder neu zu laden).</summary>
    public void Forget(HistoryItem item) {
        if(cachedNodes.Remove(item, out LinkedListNode<HistoryItem>? node)) cached.Remove(node);
    }

    private void work() {
        while(true) {
            (HistoryItem Item, int Version) next;
            lock(sync) {
                if(pending.Count == 0) {
                    running--;
                    return;
                }
                next = pending.Last!.Value;
                pending.RemoveLast();
            }
            CaptureEntry entry = next.Item.Entry;
            ImageSource? image = load(entry);
            dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => complete(next.Item, next.Version, image)));
        }
    }

    /// <summary>Vorschaubild laden; die Kennzeichen landen dabei in <see cref="CaptureEntry.Metadata"/> (einmal je Eintrag).</summary>
    private ImageSource? load(CaptureEntry entry) {
        ImageSource? image = loadThumbnail(entry);
        storage.GetMetadata(entry);
        return image;
    }

    private ImageSource? loadThumbnail(CaptureEntry entry) {
        try {
            if(entry.HasThumbnail || storage.EnsureThumbnail(entry.Files)) return ImageFiles.LoadScaled(entry.Files.ThumbPath, DecodeWidth);
        } catch(Exception ex) {
            Log.Warn($"Vorschaubild {entry.Files.ThumbPath} nicht ladbar: {ex.Message}");
        }
        // Vorschaubild defekt oder nicht erzeugbar: direkt aus dem PNG
        try {
            return ImageFiles.LoadScaled(entry.Files.PngPath, DecodeWidth);
        } catch(Exception ex) {
            Log.Warn($"Bild {entry.Files.PngPath} nicht ladbar: {ex.Message}");
            return null;
        }
    }

    private void complete(HistoryItem item, int version, ImageSource? image) {
        if(!item.SetLoaded(version, image)) return;
        loadedCount++;
        Forget(item);
        if(image == null) return;
        cachedNodes[item] = cached.AddLast(item);
        while(cached.Count > capacity) {
            HistoryItem oldest = cached.First!.Value;
            cached.RemoveFirst();
            cachedNodes.Remove(oldest);
            oldest.ReleaseThumbnail();
        }
    }
}
