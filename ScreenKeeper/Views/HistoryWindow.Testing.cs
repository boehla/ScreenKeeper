using System.Diagnostics;
using System.Security.Cryptography;
using System.Windows.Threading;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Views;

/// <summary>
/// Nur für automatisierte Tests (<c>--history --folder &lt;Ordner&gt; --test-history [--test-delete &lt;Name&gt;]</c>):
/// misst den ersten Ladeblock, scrollt bis zum Ende, filtert auf „Heute“ und löscht optional eine Aufnahme über den
/// Befehl des ViewModels; außerdem Kopie anlegen (<c>--test-duplicate</c>) und „permanent“ umschalten
/// (<c>--test-permanent</c>). Ausgaben auf stdout.
/// </summary>
public partial class HistoryWindow {
    private static readonly TimeSpan testTimeout = TimeSpan.FromSeconds(120);

    public async Task<int> RunTestAsync(Stopwatch watch, bool metrics, string? editName, string? deleteName, string? duplicateName = null, string? permanentName = null) {
        testMode = true;
        await RefreshTask;
        await waitIdle();
        if(metrics) {
            Console.Out.WriteLine($"entries={viewModel.ItemCount} groups={viewModel.GroupCount} firstBlockMs={watch.ElapsedMilliseconds} realized={countRealized()}");
            Console.Out.Flush();

            // Ohne Scrollen darf nur nachgeladen werden, bis die Liste den sichtbaren Bereich füllt
            await Task.Delay(500);
            await waitIdle();
            Console.Out.WriteLine($"idleEntries={viewModel.ItemCount} viewport={scrollViewer?.ViewportHeight:0} extent={scrollViewer?.ExtentHeight:0}");

            // Simuliertes Scrollen: immer wieder ans Ende, das Nachladen löst die Liste selbst aus
            int steps = 0;
            while(!viewModel.IsComplete || viewModel.IsLoading) {
                scrollViewer?.ScrollToEnd();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                if(viewModel.IsLoading) await Task.Delay(2);
                steps++;
                if(watch.Elapsed > testTimeout) {
                    Console.Error.WriteLine($"Zeitüberschreitung beim Nachladen ({viewModel.ItemCount} Einträge)");
                    return 1;
                }
            }
            scrollViewer?.ScrollToEnd();
            await waitIdle();
            Console.Out.WriteLine($"total={viewModel.ItemCount} groups={viewModel.GroupCount} allMs={watch.ElapsedMilliseconds} rows={viewModel.Rows.Count} scrollSteps={steps} realized={countRealized()} thumbsLoaded={thumbnails.LoadedCount} thumbsCached={thumbnails.CachedCount}");

            Stopwatch todayWatch = Stopwatch.StartNew();
            await viewModel.ApplyQuickRangeAsync(HistoryQuickRange.Today);
            await viewModel.LoadAllAsync();
            Console.Out.WriteLine($"today={viewModel.ItemCount} todayMs={todayWatch.ElapsedMilliseconds}");

            // Textfilter über Programm und Fenstertitel: liest die JSON aller Einträge (im Hintergrund, einmal je Eintrag)
            await viewModel.ApplyQuickRangeAsync(HistoryQuickRange.All);
            Stopwatch searchWatch = Stopwatch.StartNew();
            viewModel.FilterText = "zzq-kein-treffer";
            await viewModel.LoadAllAsync();
            long firstSearchMs = searchWatch.ElapsedMilliseconds;
            searchWatch.Restart();
            viewModel.FilterText = "zzq-kein-treffer2";
            await viewModel.LoadAllAsync();
            Console.Out.WriteLine($"searchMatches={viewModel.ItemCount} searchMs={firstSearchMs} searchCachedMs={searchWatch.ElapsedMilliseconds}");
            viewModel.FilterText = "";
            await viewModel.LoadAllAsync();
            Console.Out.Flush();
        }
        if(duplicateName != null) {
            int result = await runDuplicateTest(duplicateName);
            if(result != 0) return result;
        }
        if(permanentName != null) {
            int result = await runPermanentTest(permanentName);
            if(result != 0) return result;
        }
        if(editName != null) {
            int result = await runEditTest(editName);
            if(result != 0) return result;
        }
        if(deleteName != null) {
            int result = await runDeleteTest(deleteName);
            if(result != 0) return result;
        }
        allowClose = true;
        Close();
        return 0;
    }

    /// <summary>
    /// Bearbeiten über den Befehl der History: Editor öffnet (bei einem PNG ohne JSON wird eines angelegt), wird nach 1 s
    /// geschlossen, danach muss der Eintrag aktualisiert sein.
    /// </summary>
    private async Task<int> runEditTest(string name) {
        await viewModel.ApplyQuickRangeAsync(HistoryQuickRange.All);
        await viewModel.LoadAllAsync();
        HistoryItem? item = viewModel.Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if(item == null) {
            Console.Error.WriteLine($"Aufnahme {name} nicht gefunden");
            return 1;
        }
        bool hadJson = item.HasJson;
        int version = item.Version;
        list.SelectedItems.Clear();
        list.SelectedItems.Add(item);
        viewModel.EditCommand.Execute(null);
        EditorWindow? editor = AppServices.Workflow.OpenEditor;
        if(editor == null) {
            Console.Error.WriteLine("Editor wurde nicht geöffnet");
            return 1;
        }
        await Task.Delay(1000);
        bool editorActive = editor.IsActive;
        TaskCompletionSource closed = new TaskCompletionSource();
        editor.Closed += (s, e) => closed.TrySetResult();
        editor.Close();
        await closed.Task;
        await waitIdle();
        Console.Out.WriteLine($"edited={name} mode={editor.Mode} editorActive={editorActive} hadJson={hadJson} hasJson={item.HasJson} reloaded={item.Version > version}");
        Console.Out.Flush();
        return item.HasJson && item.Version > version ? 0 : 1;
    }

    private async Task<int> runDeleteTest(string name) {
        await viewModel.ApplyQuickRangeAsync(HistoryQuickRange.All);
        await viewModel.LoadAllAsync();
        HistoryItem? item = viewModel.Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if(item == null) {
            Console.Error.WriteLine($"Aufnahme {name} nicht gefunden");
            return 1;
        }
        CaptureFiles files = item.Files;
        int before = viewModel.ItemCount;
        list.SelectedItems.Clear();
        list.SelectedItems.Add(item);
        if(!viewModel.DeleteCommand.CanExecute(null)) {
            Console.Error.WriteLine("Löschen ist nicht ausführbar (keine Auswahl)");
            return 1;
        }
        viewModel.DeleteCommand.Execute(null);
        Stopwatch watch = Stopwatch.StartNew();
        while(viewModel.FindItem(files.PngPath) != null) {
            await Task.Delay(50);
            if(watch.Elapsed > TimeSpan.FromSeconds(30)) {
                Console.Error.WriteLine("Zeitüberschreitung beim Löschen");
                return 1;
            }
        }
        int left = new[] { files.PngPath, files.JsonPath, files.ThumbPath }.Count(File.Exists);
        Console.Out.WriteLine($"deleted={name} before={before} remaining={viewModel.ItemCount} filesLeft={left}");
        Console.Out.Flush();
        return left == 0 ? 0 : 1;
    }

    /// <summary>
    /// Kopie über den Befehl der History: drei neue Dateien, neue Ids, Original unverändert, Kopie neben dem Original und
    /// ausgewählt.
    /// </summary>
    private async Task<int> runDuplicateTest(string name) {
        HistoryItem? item = await findTestItem(name);
        if(item == null) return 1;
        CaptureFiles files = item.Files;
        string folder = Path.GetDirectoryName(files.PngPath) ?? "";
        HashSet<string> before = new HashSet<string>(Directory.GetFiles(folder), StringComparer.OrdinalIgnoreCase);
        string originalHash = hashFiles(files);
        List<string> originalIds = File.Exists(files.JsonPath) ? CaptureDocumentSerializer.Load(files.JsonPath).Annotations.Select(a => a.Id).ToList() : new List<string>();
        list.SelectedItems.Clear();
        list.SelectedItems.Add(item);
        HistoryItem? copy = await viewModel.DuplicateAsync(null);
        await waitIdle();
        if(copy == null) {
            Console.Error.WriteLine("Kopie wurde nicht angelegt");
            return 1;
        }
        List<string> created = Directory.GetFiles(folder).Where(f => !before.Contains(f)).ToList();
        CaptureDocument copied = CaptureDocumentSerializer.Load(copy.Files.JsonPath);
        List<string> copyIds = copied.Annotations.Select(a => a.Id).ToList();
        bool newIds = copyIds.Count == originalIds.Count && !copyIds.Intersect(originalIds).Any() && copyIds.Distinct().Count() == copyIds.Count;
        bool unchanged = hashFiles(files) == originalHash;
        int index = viewModel.Rows.IndexOf(copy);
        bool adjacent = index >= 0 && index + 1 < viewModel.Rows.Count && ReferenceEquals(viewModel.Rows[index + 1], item);
        bool selected = list.SelectedItems.Count == 1 && ReferenceEquals(list.SelectedItem, copy);
        bool imageOk = copied.Image == Path.GetFileName(copy.Files.PngPath) && !copied.Permanent;
        Console.Out.WriteLine($"duplicated={name} copy={copy.Name} newFiles={created.Count} annotations={copyIds.Count} newIds={newIds} originalUnchanged={unchanged} adjacent={adjacent} selected={selected} imageOk={imageOk}");
        Console.Out.Flush();
        return created.Count == 3 && newIds && unchanged && adjacent && selected && imageOk ? 0 : 1;
    }

    /// <summary>
    /// „Permanent“ über den Befehl der History umschalten, JSON prüfen, zurückschalten und erneut prüfen.
    /// </summary>
    private async Task<int> runPermanentTest(string name) {
        HistoryItem? item = await findTestItem(name);
        if(item == null) return 1;
        bool before = File.Exists(item.Files.JsonPath) && CaptureDocumentSerializer.Load(item.Files.JsonPath).Permanent;
        bool? first = await viewModel.TogglePermanentAsync(item);
        bool jsonFirst = readPermanent(item.Files.JsonPath);
        bool itemFirst = item.IsPermanent;
        bool? second = await viewModel.TogglePermanentAsync(item);
        bool jsonSecond = readPermanent(item.Files.JsonPath);
        bool ok = first == !before && jsonFirst == !before && itemFirst == !before && second == before && jsonSecond == before && item.IsPermanent == before;
        Console.Out.WriteLine($"permanent={name} before={before} toggled={first} json={jsonFirst} item={itemFirst} restored={jsonSecond == before} ok={ok}");
        Console.Out.Flush();
        return ok ? 0 : 1;
    }

    private async Task<HistoryItem?> findTestItem(string name) {
        await viewModel.ApplyQuickRangeAsync(HistoryQuickRange.All);
        await viewModel.LoadAllAsync();
        HistoryItem? item = viewModel.Items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
        if(item == null) Console.Error.WriteLine($"Aufnahme {name} nicht gefunden");
        return item;
    }

    private static bool readPermanent(string jsonPath) {
        if(!File.Exists(jsonPath)) return false;
        string json = File.ReadAllText(jsonPath);
        return CaptureDocumentSerializer.Deserialize(json).Permanent && json.Contains("\"permanent\": true", StringComparison.Ordinal);
    }

    /// <summary>SHA-256 der drei Dateien einer Aufnahme (fehlende zählen als „-“).</summary>
    private static string hashFiles(CaptureFiles files) {
        IEnumerable<string> hashes = new[] { files.PngPath, files.JsonPath, files.ThumbPath }
            .Select(path => File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : "-");
        return string.Join("|", hashes);
    }

    private async Task waitIdle() {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
    }

    private int countRealized() {
        int count = 0;
        for(int i = 0; i < list.Items.Count; i++) {
            if(list.ItemContainerGenerator.ContainerFromIndex(i) != null) count++;
        }
        return count;
    }
}
