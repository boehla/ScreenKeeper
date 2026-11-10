using System.Windows.Input;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Tools;

namespace ScreenKeeper.Tests;

/// <summary>
/// 0.8.0: Aufnahme im Editor verwerfen (nichts speichern, Dateien in den Papierkorb, History-Eintrag entfernen).
/// </summary>
public class DiscardCaptureTests {
    private sealed class RecordingHost : IEditorHost {
        public int CloseCount { get; private set; }

        public int DiscardCount { get; private set; }

        public PixelPoint GetCursorImagePoint() {
            return new PixelPoint(0, 0);
        }

        public string? AskSaveAsPath(string initialFolder, string fileName) {
            return null;
        }

        public void ShowError(string message) {
        }

        public void CloseEditor() {
            CloseCount++;
        }

        public void DiscardCapture() {
            DiscardCount++;
        }
    }

    private static EditorViewModel create(RecordingHost host) {
        CaptureDocument document = new CaptureDocument {
            VirtualScreen = new PixelRect(0, 0, 400, 300),
            Monitors = new List<MonitorInfo> { new MonitorInfo { Bounds = new PixelRect(0, 0, 400, 300), Primary = true } }
        };
        return new EditorViewModel(document, TestImages.CreatePattern(400, 300), host, new AppSettings());
    }

    private static PointerInput at(int x, int y) {
        return PointerInput.AtPixel(new PixelPoint(x, y), ModifierKeys.None);
    }

    [Fact]
    public void DiscardCommand_AsksHostToDiscard_NotToClose_AndCancelsRunningDrag() {
        Sta.Run(() => {
            RecordingHost host = new RecordingHost();
            EditorViewModel vm = create(host);
            vm.SetSelection(new PixelRect(10, 10, 50, 50));
            vm.PointerDown(at(100, 100));
            vm.PointerMove(at(200, 200));
            Assert.True(vm.IsGestureActive);

            Assert.True(vm.DiscardCommand.CanExecute(null));
            vm.DiscardCommand.Execute(null);

            Assert.Equal(1, host.DiscardCount);
            Assert.Equal(0, host.CloseCount);
            Assert.False(vm.IsGestureActive);
            // der abgebrochene Ziehvorgang wird nicht mehr übernommen
            Assert.Equal(new PixelRect(10, 10, 50, 50), vm.Selection);
            Assert.Equal(1, vm.UndoStack.UndoCount);
        });
    }

    [Fact]
    public void Discard_DropsOpenTextInsteadOfCommittingIt() {
        Sta.Run(() => {
            RecordingHost host = new RecordingHost();
            EditorViewModel vm = create(host);
            Assert.True(vm.SelectToolByKey(TextTool.ToolKey));
            vm.PointerDown(at(100, 100) with { ClickCount = 1 });
            vm.PointerUp(at(100, 100));
            Assert.True(vm.IsTextEditing);
            vm.UpdateEditingText("Hallo");

            vm.Discard();

            Assert.False(vm.IsTextEditing);
            Assert.Empty(vm.Document.Annotations);
            Assert.Equal(0, vm.UndoStack.UndoCount);
            Assert.Equal(1, host.DiscardCount);
            Assert.Equal(0, host.CloseCount);
        });
    }

    [Fact]
    public void Cancel_StillClosesNormally() {
        Sta.Run(() => {
            RecordingHost host = new RecordingHost();
            EditorViewModel vm = create(host);
            vm.CancelCommand.Execute(null);
            Assert.Equal(1, host.CloseCount);
            Assert.Equal(0, host.DiscardCount);
        });
    }

    [Fact]
    public async Task History_RemovesDiscardedEntry_OnlyWhenImageIsGone() {
        using (TempFolder temp = new TempFolder()) {
            SettingsService settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
            settings.Load();
            settings.Settings.StorageFolder = Path.Combine(temp.Path, "ablage");
            StorageService storage = new StorageService(settings, new RenderService());
            string folder = Path.Combine(storage.RootFolder, "2026", "11");
            Directory.CreateDirectory(folder);
            CaptureFiles kept = CaptureFiles.FromBase(folder, "2026-11-09_14-00-00");
            CaptureFiles discarded = CaptureFiles.FromBase(folder, "2026-11-09_15-00-00");
            foreach(CaptureFiles files in new[] { kept, discarded }) {
                File.WriteAllBytes(files.PngPath, new byte[] { 1 });
                File.WriteAllText(files.JsonPath, "{}");
            }
            HistoryViewModel viewModel = new HistoryViewModel(storage, null, () => new DateTime(2026, 11, 9, 18, 0, 0));
            await viewModel.RefreshAsync();
            Assert.Equal(2, viewModel.ItemCount);

            // Löschen fehlgeschlagen (PNG noch da) → Eintrag bleibt
            viewModel.OnCaptureDiscarded(discarded);
            Assert.Equal(2, viewModel.ItemCount);

            // so, wie es der Papierkorb hinterlässt: Dateien weg; Ereignis mit den Dateien aus dem JSON-Pfad
            File.Delete(discarded.PngPath);
            File.Delete(discarded.JsonPath);
            viewModel.OnCaptureDiscarded(CaptureFiles.FromJson(discarded.JsonPath));
            Assert.Equal(1, viewModel.ItemCount);
            Assert.Null(viewModel.FindItem(discarded.PngPath));
            Assert.NotNull(viewModel.FindItem(kept.PngPath));
            Assert.Equal(1, ((HistoryHeaderRow)viewModel.Rows[0]).Count);
            Assert.Contains("2026-11-09_15-00-00", viewModel.Message);

            // letzte Aufnahme des Tages verworfen → Tagesgruppe verschwindet
            File.Delete(kept.PngPath);
            viewModel.OnCaptureDiscarded(kept);
            Assert.Equal(0, viewModel.ItemCount);
            Assert.Empty(viewModel.Rows);
        }
    }
}
