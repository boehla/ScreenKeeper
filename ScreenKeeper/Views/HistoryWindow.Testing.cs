using System.Diagnostics;
using System.Windows.Threading;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Views;

/// <summary>
/// Nur für automatisierte Tests (<c>--history --folder &lt;Ordner&gt; --test-history [--test-delete &lt;Name&gt;]</c>):
/// misst den ersten Ladeblock, scrollt bis zum Ende, filtert auf „Heute“ und löscht optional eine Aufnahme über den
/// Befehl des ViewModels. Ausgaben auf stdout.
/// </summary>
public partial class HistoryWindow {
    private static readonly TimeSpan testTimeout = TimeSpan.FromSeconds(120);

    public async Task<int> RunTestAsync(Stopwatch watch, bool metrics, string? editName, string? deleteName) {
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
            Console.Out.Flush();
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
