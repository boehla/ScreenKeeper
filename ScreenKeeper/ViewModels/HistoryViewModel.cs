using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Rückfragen und Fehlermeldungen der History (vom Fenster umgesetzt).
/// </summary>
public interface IHistoryHost {
    bool Confirm(string message);

    void ShowError(string message);
}

/// <summary>
/// History: Aufnahmen neueste zuerst, nach Tagen gruppiert (flache Liste aus Überschriften und Einträgen, damit die
/// Liste virtualisiert bleibt), blockweise nachgeladen; Datums-, Text- und Permanent-Filter; Aktionen Bearbeiten,
/// Zwischenablage, Kopie anlegen, Permanent, Explorer, Löschen (Papierkorb), Aktualisieren, Ordner öffnen.
/// </summary>
public sealed class HistoryViewModel : ViewModelBase {
    public const int BlockSize = 60;

    private readonly IStorageService storage;
    private readonly IThumbnailSource? thumbnails;
    private readonly Func<DateTime> clock;
    private readonly HashSet<string> loadedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<HistoryItem> selection = new List<HistoryItem>();
    private HistoryPager? pager;
    private Task? loadingTask;
    private HistoryHeaderRow? lastHeader;
    private int generation;
    private bool isLoading;
    private bool isComplete = true;
    private int itemCount;
    private int groupCount;
    private DateTime? filterFrom;
    private DateTime? filterTo;
    private string filterText = "";
    private bool filterPermanent;
    private HistoryQuickRange? quickRange = HistoryQuickRange.All;
    private DateTime labelsDay;
    private string loadedRoot = "";
    private string message = "";
    private bool loadFailed;

    public HistoryViewModel(IStorageService storage, IThumbnailSource? thumbnails, Func<DateTime>? clock = null) {
        this.storage = storage;
        this.thumbnails = thumbnails;
        this.clock = clock ?? (() => DateTime.Now);
        Rows = new ObservableCollection<HistoryRow>();
        EditCommand = new RelayCommand(p => Edit(p as HistoryItem), p => p is HistoryItem || selection.Count > 0);
        CopyCommand = new RelayCommand(p => _ = CopyAsync(p as HistoryItem), p => p is HistoryItem || selection.Count > 0);
        ShowInExplorerCommand = new RelayCommand(p => ShowInExplorer(p as HistoryItem), p => p is HistoryItem || selection.Count > 0);
        DuplicateCommand = new RelayCommand(p => _ = DuplicateAsync(p as HistoryItem), p => p is HistoryItem || selection.Count > 0);
        TogglePermanentCommand = new RelayCommand(p => _ = TogglePermanentAsync(p as HistoryItem), p => p is HistoryItem || selection.Count > 0);
        DeleteCommand = new RelayCommand(() => _ = DeleteSelectedAsync(), () => selection.Count > 0);
        RefreshCommand = new RelayCommand(() => _ = RefreshAsync());
        OpenFolderCommand = new RelayCommand(OpenFolder);
        QuickRangeCommand = new RelayCommand(p => {
            if(p is HistoryQuickRange range) {
                _ = ApplyQuickRangeAsync(range);
            } else if(p is string text && Enum.TryParse(text, out HistoryQuickRange parsed)) {
                _ = ApplyQuickRangeAsync(parsed);
            }
        });
    }

    public IHistoryHost? Host { get; set; }

    /// <summary>Update-Hinweis für die Statusleiste; null = keiner (Tests).</summary>
    public UpdateViewModel? Update { get; set; }

    /// <summary>Überschriften und Einträge in Anzeigereihenfolge.</summary>
    public ObservableCollection<HistoryRow> Rows { get; }

    /// <summary>Nach jedem geladenen Block (im Thread des Aufrufers, normalerweise UI-Thread).</summary>
    public event EventHandler? BlockLoaded;

    /// <summary>Ein Eintrag soll ausgewählt und sichtbar gemacht werden (z. B. eine neue Kopie).</summary>
    public event EventHandler<HistoryItem>? SelectRequested;

    public ICommand EditCommand { get; }

    public ICommand CopyCommand { get; }

    public ICommand ShowInExplorerCommand { get; }

    /// <summary>Kopie der Aufnahme anlegen (Strg+D); Parameter = Eintrag, sonst der erste ausgewählte.</summary>
    public ICommand DuplicateCommand { get; }

    /// <summary>Permanent markieren bzw. Markierung aufheben (P); Parameter = Eintrag, sonst die Auswahl.</summary>
    public ICommand TogglePermanentCommand { get; }

    public ICommand DeleteCommand { get; }

    public ICommand RefreshCommand { get; }

    public ICommand OpenFolderCommand { get; }

    public ICommand QuickRangeCommand { get; }

    public string RootFolder => loadedRoot;

    public bool IsLoading {
        get => isLoading;
        private set {
            if(SetProperty(ref isLoading, value)) updateStatus();
        }
    }

    /// <summary>Alle passenden Aufnahmen sind geladen.</summary>
    public bool IsComplete {
        get => isComplete;
        private set {
            if(SetProperty(ref isComplete, value)) updateStatus();
        }
    }

    public int ItemCount {
        get => itemCount;
        private set => SetProperty(ref itemCount, value);
    }

    public int GroupCount {
        get => groupCount;
        private set => SetProperty(ref groupCount, value);
    }

    public bool IsEmpty => itemCount == 0 && isComplete;

    public IReadOnlyList<HistoryItem> SelectedItems => selection;

    public bool HasSelection => selection.Count > 0;

    public string StatusText { get; private set; } = "";

    /// <summary>Letzte Meldung einer Aktion (z. B. „in die Zwischenablage kopiert“).</summary>
    public string Message {
        get => message;
        private set => SetProperty(ref message, value);
    }

    public DateTime? FilterFrom {
        get => filterFrom;
        set {
            if(!SetProperty(ref filterFrom, value?.Date)) return;
            setQuickRange(null);
            _ = RefreshAsync();
        }
    }

    public DateTime? FilterTo {
        get => filterTo;
        set {
            if(!SetProperty(ref filterTo, value?.Date)) return;
            setQuickRange(null);
            _ = RefreshAsync();
        }
    }

    public string FilterText {
        get => filterText;
        set {
            if(!SetProperty(ref filterText, value ?? "")) return;
            _ = RefreshAsync();
        }
    }

    /// <summary>Nur permanent markierte Aufnahmen zeigen (zusätzlich zum Zeitraum).</summary>
    public bool FilterPermanent {
        get => filterPermanent;
        set {
            if(!SetProperty(ref filterPermanent, value)) return;
            _ = RefreshAsync();
        }
    }

    public HistoryQuickRange? QuickRange => quickRange;

    public bool IsQuickAll => quickRange == HistoryQuickRange.All;

    public bool IsQuickToday => quickRange == HistoryQuickRange.Today;

    public bool IsQuick7Days => quickRange == HistoryQuickRange.Last7Days;

    public bool IsQuick30Days => quickRange == HistoryQuickRange.Last30Days;

    public HistoryFilter CurrentFilter => new HistoryFilter(filterFrom, filterTo, filterText, filterPermanent);

    /// <summary>
    /// Liste neu aufbauen und den ersten Block laden. Die Aufgabe ist fertig, sobald der erste Block angezeigt wird.
    /// </summary>
    public Task RefreshAsync() {
        generation++;
        loadingTask = null;
        (thumbnails as ThumbnailLoader)?.Clear();
        Rows.Clear();
        loadedPaths.Clear();
        clearSelection();
        lastHeader = null;
        ItemCount = 0;
        GroupCount = 0;
        loadFailed = false;
        loadedRoot = storage.RootFolder;
        OnPropertyChanged(nameof(RootFolder));
        labelsDay = clock().Date;
        HistoryFilter filter = CurrentFilter;
        pager = new HistoryPager(storage.EnumerateCaptures(filter.From, filter.To), filter, storage.GetMetadata);
        isLoading = false;
        IsComplete = false;
        OnPropertyChanged(nameof(IsEmpty));
        return LoadMoreAsync();
    }

    /// <summary>
    /// Nächsten Block laden (beim Scrollen). Läuft bereits ein Ladevorgang, wird dessen Aufgabe geliefert.
    /// </summary>
    public Task LoadMoreAsync() {
        if(loadingTask != null && !loadingTask.IsCompleted) return loadingTask;
        if(pager == null || isComplete) return Task.CompletedTask;
        loadingTask = loadBlockAsync(pager, generation);
        return loadingTask;
    }

    /// <summary>Alle restlichen Blöcke laden.</summary>
    public async Task LoadAllAsync() {
        while(!isComplete && pager != null) {
            await LoadMoreAsync();
        }
    }

    public Task ApplyQuickRangeAsync(HistoryQuickRange range) {
        HistoryFilter filter = HistoryFilter.ForQuickRange(range, clock(), filterText, filterPermanent);
        filterFrom = filter.From;
        filterTo = filter.To;
        OnPropertyChanged(nameof(FilterFrom));
        OnPropertyChanged(nameof(FilterTo));
        setQuickRange(range);
        return RefreshAsync();
    }

    /// <summary>Auswahl aus der Liste übernehmen (Überschriften werden ignoriert).</summary>
    public void SetSelection(IEnumerable<object> items) {
        selection.Clear();
        foreach(object item in items) {
            if(item is HistoryItem historyItem && !selection.Contains(historyItem)) selection.Add(historyItem);
        }
        OnPropertyChanged(nameof(HasSelection));
        CommandManager.InvalidateRequerySuggested();
    }

    /// <summary>
    /// Nach Mitternacht „Heute“/„Gestern“ neu beschriften; eine relative Schnellwahl wird neu angewendet.
    /// </summary>
    public void RefreshDayLabels() {
        DateTime today = clock().Date;
        if(today == labelsDay) return;
        labelsDay = today;
        if(quickRange.HasValue && quickRange.Value != HistoryQuickRange.All) {
            _ = ApplyQuickRangeAsync(quickRange.Value);
            return;
        }
        foreach(HistoryRow row in Rows) {
            if(row is HistoryHeaderRow header) header.Label = HistoryGrouping.GetLabel(header.Day, today);
        }
    }

    /// <summary>Ablageordner geändert (Einstellungen) → neu laden.</summary>
    public void OnStorageMaybeChanged() {
        if(!string.Equals(storage.RootFolder, loadedRoot, StringComparison.OrdinalIgnoreCase)) _ = RefreshAsync();
    }

    /// <summary>
    /// Neue Aufnahme oben einfügen (falls sie zum Filter passt und noch nicht in der Liste ist).
    /// </summary>
    public void AddNewCapture(CaptureEntry entry) {
        if(!CurrentFilter.Matches(entry) || loadedPaths.Contains(entry.Files.PngPath)) return;
        if(!isUnderRoot(entry.Files.PngPath)) return;
        loadedPaths.Add(entry.Files.PngPath);
        DateTime day = HistoryGrouping.GetDay(entry.Time);
        HistoryHeaderRow? first = Rows.Count > 0 ? Rows[0] as HistoryHeaderRow : null;
        if(first == null || first.Day != day) {
            first = new HistoryHeaderRow(day, HistoryGrouping.GetLabel(day, clock()));
            Rows.Insert(0, first);
            GroupCount++;
            lastHeader ??= first;
        }
        Rows.Insert(1, new HistoryItem(entry, thumbnails));
        first.Count++;
        ItemCount++;
        OnPropertyChanged(nameof(IsEmpty));
        updateStatus();
    }

    /// <summary>
    /// Kopie direkt über dem Original einfügen (gleiche Aufnahmezeit, der Name mit Suffix sortiert davor) und auswählen.
    /// Ist das Original nicht geladen, erscheint die Kopie erst beim nächsten Laden. Liefert den Eintrag der Kopie.
    /// </summary>
    public HistoryItem? InsertCopy(string originalPath, CaptureEntry copy) {
        HistoryItem? original = FindItem(Path.GetFullPath(originalPath));
        if(original == null) return null;
        HistoryItem? existing = FindItem(copy.Files.PngPath);
        if(existing != null) {
            SelectRequested?.Invoke(this, existing);
            return existing;
        }
        int index = Rows.IndexOf(original);
        int headerIndex = index - 1;
        while(headerIndex >= 0 && Rows[headerIndex] is not HistoryHeaderRow) headerIndex--;
        HistoryItem item = new HistoryItem(copy, thumbnails);
        loadedPaths.Add(copy.Files.PngPath);
        Rows.Insert(index, item);
        if(headerIndex >= 0 && Rows[headerIndex] is HistoryHeaderRow header) header.Count++;
        ItemCount++;
        OnPropertyChanged(nameof(IsEmpty));
        updateStatus();
        SelectRequested?.Invoke(this, item);
        return item;
    }

    /// <summary>
    /// Kopie anlegen (PNG, JSON mit neuen Ids, Vorschaubild) und neben dem Original einfügen und auswählen.
    /// </summary>
    public async Task<HistoryItem?> DuplicateAsync(HistoryItem? item) {
        item ??= selection.FirstOrDefault();
        if(item == null) return null;
        CaptureFiles source = item.Files;
        try {
            CaptureFiles copy = await Task.Run(() => storage.DuplicateCapture(source.PngPath));
            CaptureEntry? entry = storage.GetEntry(copy);
            if(entry == null) return null;
            Message = Strings.Format(Strings.HistoryDuplicated, entry.Name);
            return InsertCopy(source.PngPath, entry);
        } catch(Exception ex) {
            Log.Error(ex, $"Kopie von {source.PngPath} fehlgeschlagen");
            Host?.ShowError(Strings.Format(Strings.DuplicateFailed, ex.Message));
            return null;
        }
    }

    /// <summary>
    /// Permanent umschalten: sind alle betroffenen Einträge permanent, wird die Markierung aufgehoben, sonst gesetzt.
    /// Schreibt die JSON (fehlt sie, wird sie angelegt); ein offener Editor derselben Aufnahme übernimmt den Wert.
    /// Liefert den neuen Wert (null, wenn nichts geändert wurde).
    /// </summary>
    public async Task<bool?> TogglePermanentAsync(HistoryItem? item) {
        List<HistoryItem> items = item != null ? new List<HistoryItem> { item } : selection.ToList();
        if(items.Count == 0) return null;
        List<CaptureEntry> entries = items.Select(i => i.Entry).ToList();
        int changed = 0;
        bool value = false;
        try {
            value = !await Task.Run(() => entries.All(e => storage.GetMetadata(e).Permanent));
            foreach(HistoryItem target in items) {
                CaptureFiles files = target.Files;
                await Task.Run(() => storage.SetPermanent(files.PngPath, value));
                applyToOpenEditor(files.JsonPath, value);
                CaptureEntry? updated = storage.GetEntry(files);
                if(updated != null) {
                    storage.GetMetadata(updated);
                    target.UpdateEntry(updated);
                }
                changed++;
            }
        } catch(Exception ex) {
            Log.Error(ex, "Markierung „permanent“ konnte nicht gespeichert werden");
            Host?.ShowError(Strings.Format(Strings.HistoryPermanentFailed, ex.Message));
        }
        if(changed == 0) return null;
        Message = value ? Strings.Format(Strings.HistoryPermanentOn, changed) : Strings.Format(Strings.HistoryPermanentOff, changed);
        return value;
    }

    /// <summary>
    /// Editor geschlossen: Eintrag aktualisieren (Vorschaubild neu laden, Kennzeichen „bearbeitet“).
    /// </summary>
    public void OnEditorClosed(string jsonPath) {
        string fullPath = Path.GetFullPath(jsonPath);
        HistoryItem? item = FindItem(fullPath);
        if(item == null) return;
        CaptureEntry? updated = storage.GetEntry(item.Files);
        if(updated == null) return;
        (thumbnails as ThumbnailLoader)?.Forget(item);
        item.Reload(updated);
    }

    /// <summary>
    /// Aufnahme im Editor verworfen: Eintrag entfernen, sobald das PNG weg ist (schlug das Löschen fehl, bleibt er stehen).
    /// </summary>
    public void OnCaptureDiscarded(CaptureFiles files) {
        HistoryItem? item = FindItem(Path.GetFullPath(files.PngPath));
        if(item == null || File.Exists(item.Files.PngPath)) return;
        string name = item.Name;
        RemoveItems(new[] { item });
        Message = Strings.Format(Strings.HistoryDiscarded, name);
    }

    public HistoryItem? FindItem(string path) {
        foreach(HistoryRow row in Rows) {
            if(row is HistoryItem item && (string.Equals(item.Files.JsonPath, path, StringComparison.OrdinalIgnoreCase) || string.Equals(item.Files.PngPath, path, StringComparison.OrdinalIgnoreCase))) return item;
        }
        return null;
    }

    public IEnumerable<HistoryItem> Items => Rows.OfType<HistoryItem>();

    /// <summary>
    /// Bearbeiten: öffnet den Editor (legt für ein PNG ohne JSON zuerst ein Dokument an).
    /// </summary>
    public void Edit(HistoryItem? item) {
        item ??= selection.FirstOrDefault();
        if(item == null) return;
        try {
            AppServices.Workflow.OpenOrCreate(item.Files.PngPath);
            if(!item.HasJson) {
                CaptureEntry? updated = storage.GetEntry(item.Files);
                if(updated != null) item.Reload(updated);
            }
        } catch(Exception ex) {
            Log.Error(ex, $"Aufnahme {item.Files.PngPath} konnte nicht geöffnet werden");
            Host?.ShowError(Strings.Format(Strings.HistoryEditFailed, ex.Message));
        }
    }

    /// <summary>
    /// In die Zwischenablage: gerenderter Bereich, ohne Bereich das ganze Bild (mit Zeichnungen und Maus).
    /// </summary>
    public async Task CopyAsync(HistoryItem? item) {
        item ??= selection.FirstOrDefault();
        if(item == null) return;
        try {
            CaptureEntry entry = item.Entry;
            (CaptureDocument? document, BitmapSource original) = await Task.Run(() => loadForCopy(entry));
            BitmapSource image = original;
            if(document != null) {
                PixelRect region = document.Selection ?? new PixelRect(0, 0, original.PixelWidth, original.PixelHeight);
                image = AppServices.Render.Render(document, original, region);
            }
            AppServices.Clipboard.SetImage(image);
            Message = Strings.Format(Strings.HistoryCopied, item.Name);
            Log.Info($"History: {item.Files.PngPath} in die Zwischenablage kopiert ({image.PixelWidth}×{image.PixelHeight})");
        } catch(Exception ex) {
            Log.Error(ex, $"Kopieren von {item.Files.PngPath} fehlgeschlagen");
            Host?.ShowError(Strings.Format(Strings.ErrorClipboardFailed, ex.Message));
        }
    }

    public void ShowInExplorer(HistoryItem? item) {
        item ??= selection.FirstOrDefault();
        if(item == null) return;
        try {
            ProcessStartInfo info = new ProcessStartInfo("explorer.exe", $"/select,\"{item.Files.PngPath}\"") { UseShellExecute = false };
            using (Process? process = Process.Start(info)) {
            }
        } catch(Exception ex) {
            Log.Error(ex, "Explorer konnte nicht gestartet werden");
            Host?.ShowError(ex.Message);
        }
    }

    public void OpenFolder() {
        try {
            string root = storage.RootFolder;
            Directory.CreateDirectory(root);
            ProcessStartInfo info = new ProcessStartInfo(root) { UseShellExecute = true };
            using (Process? process = Process.Start(info)) {
            }
        } catch(Exception ex) {
            Log.Error(ex, "Ablageordner konnte nicht geöffnet werden");
            Host?.ShowError(ex.Message);
        }
    }

    /// <summary>
    /// Ausgewählte Aufnahmen nach Rückfrage in den Papierkorb (PNG, JSON, Vorschaubild). Liefert die Zahl der entfernten.
    /// </summary>
    public async Task<int> DeleteSelectedAsync() {
        List<HistoryItem> items = selection.ToList();
        if(items.Count == 0) return 0;
        string? editing = AppServices.IsInitialized ? AppServices.Workflow.OpenEditor?.JsonPath : null;
        if(editing != null && items.Any(i => string.Equals(Path.GetFullPath(i.Files.JsonPath), Path.GetFullPath(editing), StringComparison.OrdinalIgnoreCase))) {
            Host?.ShowError(Strings.HistoryDeleteInEditor);
            return 0;
        }
        string question = items.Count == 1 ? Strings.Format(Strings.HistoryDeleteConfirmOne, items[0].Name) : Strings.Format(Strings.HistoryDeleteConfirmMany, items.Count);
        if(Host == null || !Host.Confirm(question)) return 0;
        List<CaptureFiles> files = items.Select(i => i.Files).ToList();
        try {
            await StaTask.Run(() => storage.DeleteToRecycleBin(files));
        } catch(Exception ex) {
            Log.Error(ex, "Löschen in den Papierkorb fehlgeschlagen");
            Host.ShowError(Strings.Format(Strings.HistoryDeleteFailed, ex.Message));
        }
        List<HistoryItem> removed = items.Where(i => !File.Exists(i.Files.PngPath)).ToList();
        RemoveItems(removed);
        Log.Info($"History: {removed.Count} Aufnahme(n) in den Papierkorb verschoben");
        Message = Strings.Format(Strings.HistoryDeleted, removed.Count);
        return removed.Count;
    }

    /// <summary>Einträge aus der Liste nehmen; leere Tagesgruppen verschwinden.</summary>
    public void RemoveItems(IEnumerable<HistoryItem> items) {
        foreach(HistoryItem item in items) {
            int index = Rows.IndexOf(item);
            if(index < 0) continue;
            int headerIndex = index - 1;
            while(headerIndex >= 0 && Rows[headerIndex] is not HistoryHeaderRow) headerIndex--;
            Rows.RemoveAt(index);
            loadedPaths.Remove(item.Files.PngPath);
            selection.Remove(item);
            (thumbnails as ThumbnailLoader)?.Forget(item);
            item.ReleaseThumbnail();
            ItemCount--;
            if(headerIndex >= 0 && Rows[headerIndex] is HistoryHeaderRow header) {
                header.Count--;
                if(header.Count <= 0) {
                    Rows.RemoveAt(headerIndex);
                    GroupCount--;
                }
            }
        }
        lastHeader = Rows.OfType<HistoryHeaderRow>().LastOrDefault();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(IsEmpty));
        updateStatus();
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task loadBlockAsync(HistoryPager current, int loadGeneration) {
        IsLoading = true;
        try {
            IReadOnlyList<CaptureEntry> block = await Task.Run(() => current.NextBlock(BlockSize));
            if(loadGeneration != generation) return;
            append(block);
            IsComplete = current.IsComplete;
            BlockLoaded?.Invoke(this, EventArgs.Empty);
        } catch(Exception ex) {
            Log.Error(ex, "History konnte nicht geladen werden");
            if(loadGeneration == generation) {
                loadFailed = true;
                IsComplete = true;
                Message = Strings.Format(Strings.HistoryLoadFailed, ex.Message);
            }
        } finally {
            if(loadGeneration == generation) {
                IsLoading = false;
                OnPropertyChanged(nameof(IsEmpty));
            }
        }
    }

    private void append(IReadOnlyList<CaptureEntry> block) {
        DateTime today = clock();
        foreach(CaptureEntry entry in block) {
            if(!loadedPaths.Add(entry.Files.PngPath)) continue;
            DateTime day = HistoryGrouping.GetDay(entry.Time);
            if(lastHeader == null || lastHeader.Day != day) {
                lastHeader = new HistoryHeaderRow(day, HistoryGrouping.GetLabel(day, today));
                Rows.Add(lastHeader);
                GroupCount++;
            }
            Rows.Add(new HistoryItem(entry, thumbnails));
            lastHeader.Count++;
            ItemCount++;
        }
        updateStatus();
    }

    private void setQuickRange(HistoryQuickRange? range) {
        quickRange = range;
        OnPropertyChanged(nameof(QuickRange));
        OnPropertyChanged(nameof(IsQuickAll));
        OnPropertyChanged(nameof(IsQuickToday));
        OnPropertyChanged(nameof(IsQuick7Days));
        OnPropertyChanged(nameof(IsQuick30Days));
    }

    private void clearSelection() {
        if(selection.Count == 0) return;
        selection.Clear();
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>Ist dieselbe Aufnahme im Editor offen, übernimmt er den Wert (sonst überschriebe er ihn beim Schließen).</summary>
    private static void applyToOpenEditor(string jsonPath, bool permanent) {
        if(!AppServices.IsInitialized) return;
        Views.EditorWindow? editor = AppServices.Workflow.OpenEditor;
        if(editor == null || !string.Equals(Path.GetFullPath(editor.JsonPath), Path.GetFullPath(jsonPath), StringComparison.OrdinalIgnoreCase)) return;
        editor.ViewModel.IsPermanent = permanent;
    }

    private bool isUnderRoot(string path) {
        if(string.IsNullOrEmpty(loadedRoot)) return false;
        string root = loadedRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private (CaptureDocument? Document, BitmapSource Original) loadForCopy(CaptureEntry entry) {
        CaptureDocument? document = null;
        string pngPath = entry.Files.PngPath;
        if(File.Exists(entry.Files.JsonPath)) {
            try {
                document = storage.LoadDocument(entry.Files.JsonPath);
                pngPath = storage.ResolveImagePath(document, entry.Files.JsonPath);
            } catch(Exception ex) {
                Log.Warn($"Dokument {entry.Files.JsonPath} nicht lesbar, kopiere das Original: {ex.Message}");
                document = null;
            }
        }
        return (document, storage.LoadOriginal(pngPath));
    }

    private void updateStatus() {
        string text;
        if(loadFailed) {
            text = Message;
        } else if(itemCount == 0 && !isComplete) {
            text = Strings.HistoryStatusLoading;
        } else if(itemCount == 0) {
            text = CurrentFilter.IsEmpty ? Strings.HistoryStatusEmpty : Strings.HistoryStatusNoMatch;
        } else if(isComplete) {
            text = Strings.Format(Strings.HistoryStatusCount, itemCount);
        } else {
            text = Strings.Format(Strings.HistoryStatusMore, itemCount);
        }
        if(text == StatusText) return;
        StatusText = text;
        OnPropertyChanged(nameof(StatusText));
    }
}
