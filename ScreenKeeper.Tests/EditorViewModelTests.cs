using System.Windows.Input;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Tools;

namespace ScreenKeeper.Tests;

public class EditorViewModelTests {
    private sealed class FakeHost : IEditorHost {
        public PixelPoint Cursor { get; set; }

        public int CloseCount { get; private set; }

        public List<string> Errors { get; } = new List<string>();

        public PixelPoint GetCursorImagePoint() {
            return Cursor;
        }

        public string? AskSaveAsPath(string initialFolder, string fileName) {
            return null;
        }

        public void ShowError(string message) {
            Errors.Add(message);
        }

        public void CloseEditor() {
            CloseCount++;
        }
    }

    private static EditorViewModel create(FakeHost host) {
        CaptureDocument document = new CaptureDocument {
            VirtualScreen = new PixelRect(-1920, 0, 3840, 1080),
            Monitors = new List<MonitorInfo> {
                new MonitorInfo { Bounds = new PixelRect(0, 0, 1920, 1080) },
                new MonitorInfo { Bounds = new PixelRect(1920, 0, 1920, 1080), Primary = true }
            }
        };
        return new EditorViewModel(document, TestImages.CreatePattern(3840, 1080), host);
    }

    private static PointerInput at(int x, int y) {
        return PointerInput.AtPixel(new PixelPoint(x, y), ModifierKeys.None);
    }

    [Fact]
    public void Drag_CreatesSelection_AsOneUndoStep() {
        Sta.Run(() => {
            EditorViewModel vm = create(new FakeHost());
            int overlayChanges = 0;
            vm.OverlayChanged += (s, e) => overlayChanges++;
            vm.PointerDown(at(100, 100));
            for(int i = 1; i <= 50; i++) vm.PointerMove(at(100 + i, 100 + i));
            Assert.Equal(new PixelRect(100, 100, 51, 51), vm.PreviewSelection);
            Assert.Null(vm.Selection);
            Assert.NotNull(vm.MagnifierFocus);
            Assert.True(vm.IsGestureActive);
            vm.PointerUp(at(150, 150));

            Assert.Equal(new PixelRect(100, 100, 51, 51), vm.Selection);
            Assert.Null(vm.PreviewSelection);
            Assert.Equal(1, vm.UndoStack.UndoCount);
            Assert.True(overlayChanges > 50);
            Assert.True(vm.CopySelectionCommand.CanExecute(null));

            vm.Undo();
            Assert.Null(vm.Selection);
            Assert.False(vm.CopySelectionCommand.CanExecute(null));
            vm.Redo();
            Assert.Equal(new PixelRect(100, 100, 51, 51), vm.Selection);
        });
    }

    [Fact]
    public void Click_WithoutDrag_DoesNothing() {
        Sta.Run(() => {
            EditorViewModel vm = create(new FakeHost());
            vm.PointerDown(at(10, 10));
            vm.PointerUp(at(10, 10));
            Assert.Null(vm.Selection);
            Assert.Equal(0, vm.UndoStack.UndoCount);
        });
    }

    [Fact]
    public void Drag_IsClampedToImage() {
        Sta.Run(() => {
            EditorViewModel vm = create(new FakeHost());
            vm.PointerDown(at(3800, 1000));
            vm.PointerMove(at(5000, 3000));
            vm.PointerUp(at(5000, 3000));
            Assert.Equal(new PixelRect(3800, 1000, 40, 80), vm.Selection);
        });
    }

    [Fact]
    public void Handle_And_Move_ChangeSelection() {
        Sta.Run(() => {
            EditorViewModel vm = create(new FakeHost());
            vm.SetSelection(new PixelRect(100, 100, 200, 100));
            // rechte Kante (Pixel 299) greifen und 7 Pixel nach rechts ziehen
            vm.PointerDown(PointerInput.FromImage(300, 150, ModifierKeys.None));
            Assert.Equal(EditorCursor.SizeWE, vm.GetCursor(at(300, 150)));
            vm.PointerMove(PointerInput.FromImage(307, 150, ModifierKeys.None));
            Assert.Equal(new PixelPoint(306, 150), vm.MagnifierFocus);
            vm.PointerUp(PointerInput.FromImage(307, 150, ModifierKeys.None));
            Assert.Equal(new PixelRect(100, 100, 207, 100), vm.Selection);

            // im Inneren verschieben
            Assert.Equal(EditorCursor.SizeAll, vm.GetCursor(at(200, 150)));
            vm.PointerDown(at(200, 150));
            vm.PointerMove(at(210, 140));
            Assert.Null(vm.MagnifierFocus);
            vm.PointerUp(at(210, 140));
            Assert.Equal(new PixelRect(110, 90, 207, 100), vm.Selection);
            Assert.Equal(3, vm.UndoStack.UndoCount);

            vm.Undo();
            vm.Undo();
            Assert.Equal(new PixelRect(100, 100, 200, 100), vm.Selection);
        });
    }

    [Fact]
    public void Drag_OutsideSelection_ReplacesIt() {
        Sta.Run(() => {
            EditorViewModel vm = create(new FakeHost());
            vm.SetSelection(new PixelRect(100, 100, 50, 50));
            vm.PointerDown(at(500, 500));
            vm.PointerMove(at(520, 530));
            vm.PointerUp(at(520, 530));
            Assert.Equal(new PixelRect(500, 500, 21, 31), vm.Selection);
        });
    }

    [Fact]
    public void Cancel_DuringDrag_KeepsOldSelection() {
        Sta.Run(() => {
            EditorViewModel vm = create(new FakeHost());
            vm.SetSelection(new PixelRect(100, 100, 50, 50));
            vm.PointerDown(at(500, 500));
            vm.PointerMove(at(600, 600));
            vm.CancelGesture();
            vm.PointerUp(at(600, 600));
            Assert.Equal(new PixelRect(100, 100, 50, 50), vm.Selection);
            Assert.Equal(1, vm.UndoStack.UndoCount);
        });
    }

    [Fact]
    public void Keyboard_StartAndEndPoint_WithArrowSteps() {
        Sta.Run(() => {
            EditorViewModel vm = create(new FakeHost());
            vm.PointerMove(at(40, 50));
            vm.KeyboardMark();
            Assert.True(vm.IsKeyboardSelecting);
            Assert.Equal(new PixelRect(40, 50, 1, 1), vm.PreviewSelection);
            // so, wie das Fenster die Pfeiltasten umsetzt: 1 Pixel bzw. 10 mit Shift
            PixelPoint cursor = vm.CursorPixel;
            cursor = SelectionGeometry.MoveCursor(cursor, 1, 0, false, vm.ImageBounds);
            cursor = SelectionGeometry.MoveCursor(cursor, 0, 1, true, vm.ImageBounds);
            vm.PointerMove(PointerInput.AtPixel(cursor, ModifierKeys.None));
            Assert.Equal(new PixelRect(40, 50, 2, 11), vm.PreviewSelection);
            vm.KeyboardMark();
            Assert.False(vm.IsKeyboardSelecting);
            Assert.Equal(new PixelRect(40, 50, 2, 11), vm.Selection);
            Assert.Equal(1, vm.UndoStack.UndoCount);
        });
    }

    [Fact]
    public void MagnifierFocus_FollowsCursorBeforeFirstSelection() {
        Sta.Run(() => {
            EditorViewModel vm = create(new FakeHost());
            vm.PointerMove(at(5, 6));
            Assert.Equal(new PixelPoint(5, 6), vm.MagnifierFocus);
            vm.SetSelection(new PixelRect(0, 0, 10, 10));
            vm.PointerMove(at(50, 60));
            Assert.Null(vm.MagnifierFocus);
        });
    }

    [Fact]
    public void MonitorRegion_UsesMonitorUnderCursor() {
        Sta.Run(() => {
            EditorViewModel vm = create(new FakeHost());
            Assert.Equal(new PixelRect(0, 0, 1920, 1080), vm.GetMonitorRegion(new PixelPoint(5, 5)));
            Assert.Equal(new PixelRect(1920, 0, 1920, 1080), vm.GetMonitorRegion(new PixelPoint(1920, 0)));
            Assert.Equal(new PixelRect(1920, 0, 1920, 1080), vm.GetMonitorRegion(new PixelPoint(5000, 500)));
        });
    }

    [Fact]
    public void ExportPath_IsInExportFolderWithCollisionSuffix() {
        using (TempFolder temp = new TempFolder()) {
            string folder = Path.Combine(temp.Path, "Export");
            DateTime time = new DateTime(2026, 11, 9, 14, 32, 5);
            string first = EditorViewModel.GetExportPath(folder, AppSettings.DefaultFileNamePattern, time);
            Assert.Equal(Path.Combine(folder, "2026-11-09_14-32-05.png"), first);
            Directory.CreateDirectory(folder);
            File.WriteAllText(first, "");
            Assert.Equal(Path.Combine(folder, "2026-11-09_14-32-05_2.png"), EditorViewModel.GetExportPath(folder, AppSettings.DefaultFileNamePattern, time));
            Assert.True(EditorViewModel.IsJpegPath(@"C:\a\b.JPG"));
            Assert.True(EditorViewModel.IsJpegPath("b.jpeg"));
            Assert.False(EditorViewModel.IsJpegPath("b.png"));
        }
    }

    [Fact]
    public void Tools_ContainRegionToolActive() {
        Sta.Run(() => {
            EditorViewModel vm = create(new FakeHost());
            ToolItem item = vm.Tools[0];
            Assert.True(item.IsActive);
            Assert.Single(vm.Tools, t => t.IsActive);
            Assert.Same(vm.SelectionTool, vm.ActiveTool);
            Assert.Equal(SelectionTool.ToolKey, item.Tool.Key);
        });
    }
}
