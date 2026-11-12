using System.ComponentModel;
using System.Windows.Input;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Tools;

namespace ScreenKeeper.Tests;

/// <summary>
/// 0.13.0: Nummer des ausgewählten Nummern-Stempels mit „−“/„+“ (Werkzeugleiste bzw. Tasten -/+) ändern – 1–999,
/// je ein Rückgängig-Schritt; der nächste Stempel nimmt weiterhin die höchste Nummer + 1.
/// </summary>
public class StampNumberTests {
    private static EditorViewModel create() {
        CaptureDocument document = new CaptureDocument {
            VirtualScreen = new PixelRect(0, 0, 400, 300),
            Monitors = new List<MonitorInfo> { new MonitorInfo { Bounds = new PixelRect(0, 0, 400, 300), Primary = true } }
        };
        return new EditorViewModel(document, TestImages.CreatePattern(400, 300), new TestEditorHost(), new AppSettings());
    }

    private static PointerInput at(int x, int y, int clicks = 0) {
        return PointerInput.AtPixel(new PixelPoint(x, y), ModifierKeys.None) with { ClickCount = clicks };
    }

    private static void click(EditorViewModel vm, string tool, int x, int y) {
        if(tool.Length > 0) Assert.True(vm.SelectToolByKey(tool));
        vm.PointerDown(at(x, y, 1));
        vm.PointerUp(at(x, y));
    }

    private static int[] numbers(EditorViewModel vm) {
        return vm.Document.Annotations.OfType<NumberStampAnnotation>().Select(s => s.Number).ToArray();
    }

    [Fact]
    public void PlusMinus_ChangeSelectedStamp_EachStepIsUndoable() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            click(vm, NumberStampTool.ToolKey, 50, 50);
            Assert.Equal(new[] { 1 }, numbers(vm));
            Assert.Equal("1", vm.CurrentStampNumberText);
            int steps = vm.UndoStack.UndoCount;

            vm.IncreaseStampNumberCommand.Execute(null);
            Assert.Equal(new[] { 2 }, numbers(vm));
            Assert.Equal("2", vm.CurrentStampNumberText);
            Assert.True(vm.ChangeSelectedStampNumber(1));
            Assert.Equal(new[] { 3 }, numbers(vm));
            vm.DecreaseStampNumberCommand.Execute(null);
            Assert.Equal(new[] { 2 }, numbers(vm));
            Assert.Equal(steps + 3, vm.UndoStack.UndoCount);

            // Rückgängig/Wiederholen: Nummer und Anzeige folgen, der Stempel bleibt ausgewählt
            Assert.True(vm.UndoStack.Undo());
            Assert.Equal(new[] { 3 }, numbers(vm));
            Assert.Equal("3", vm.CurrentStampNumberText);
            Assert.True(vm.UndoStack.Undo());
            Assert.Equal("2", vm.CurrentStampNumberText);
            Assert.True(vm.UndoStack.Redo());
            Assert.Equal(new[] { 3 }, numbers(vm));
            Assert.Equal("3", vm.CurrentStampNumberText);
            Assert.True(vm.ShowStampNumberOption);
        });
    }

    [Fact]
    public void Number_IsClampedTo1And999_WithoutUndoStepAtTheEdge() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            click(vm, NumberStampTool.ToolKey, 50, 50);
            int steps = vm.UndoStack.UndoCount;
            Assert.True(vm.ChangeSelectedStampNumber(-1));
            Assert.Equal(new[] { 1 }, numbers(vm));
            Assert.Equal(steps, vm.UndoStack.UndoCount);

            vm.Document.Annotations.Add(new NumberStampAnnotation { X = 200, Y = 100, Number = 998 });
            click(vm, ObjectSelectTool.ToolKey, 200, 100);
            vm.IncreaseStampNumberCommand.Execute(null);
            Assert.Equal("999", vm.CurrentStampNumberText);
            steps = vm.UndoStack.UndoCount;
            vm.IncreaseStampNumberCommand.Execute(null);
            Assert.Equal("999", vm.CurrentStampNumberText);
            Assert.Equal(steps, vm.UndoStack.UndoCount);

            // größere Schritte und Werte außerhalb (z. B. aus einer Datei) werden in den Bereich geholt
            Assert.True(vm.ChangeSelectedStampNumber(-5000));
            Assert.Equal("1", vm.CurrentStampNumberText);
        });
    }

    [Fact]
    public void NextStamp_IsMaxPlusOne_DuplicatesAllowed() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            click(vm, NumberStampTool.ToolKey, 50, 50);
            for(int i = 0; i < 3; i++) vm.IncreaseStampNumberCommand.Execute(null);
            Assert.Equal(new[] { 4 }, numbers(vm));
            click(vm, "", 150, 50);
            Assert.Equal(new[] { 4, 5 }, numbers(vm));
            Assert.Equal("5", vm.CurrentStampNumberText);

            // der neue Stempel ist ausgewählt; runter auf 4 ergibt eine doppelte Nummer, der nächste bekommt 5
            vm.DecreaseStampNumberCommand.Execute(null);
            Assert.Equal(new[] { 4, 4 }, numbers(vm));
            click(vm, "", 250, 50);
            Assert.Equal(new[] { 4, 4, 5 }, numbers(vm));
        });
    }

    [Fact]
    public void Buttons_OnlyForSelectedStamp_NotForToolAlone() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.True(vm.SelectToolByKey(NumberStampTool.ToolKey));
            Assert.True(vm.ShowStampSizeOption);
            Assert.False(vm.ShowStampNumberOption);
            Assert.Equal("", vm.CurrentStampNumberText);
            // ohne ausgewählten Stempel ändert sich nichts (die Tasten gehen dann an die Lupe)
            Assert.False(vm.ChangeSelectedStampNumber(1));

            click(vm, "", 50, 50);
            Assert.True(vm.ShowStampNumberOption);

            // anderes Objekt ausgewählt: keine Knöpfe
            Assert.True(vm.SelectToolByKey(RectangleTool.ToolKey));
            vm.PointerDown(at(100, 100, 1));
            vm.PointerMove(at(150, 140));
            vm.PointerUp(at(150, 140));
            Assert.IsType<RectangleAnnotation>(vm.SelectedAnnotation);
            Assert.False(vm.ShowStampNumberOption);
            Assert.False(vm.ChangeSelectedStampNumber(1));
            Assert.Equal(new[] { 1 }, numbers(vm));

            // Stempel mit „Auswahl“ wählen, Auswahl aufheben
            click(vm, ObjectSelectTool.ToolKey, 50, 50);
            Assert.True(vm.ShowStampNumberOption);
            vm.SelectAnnotation(null);
            Assert.False(vm.ShowStampNumberOption);
            Assert.Equal("", vm.CurrentStampNumberText);
        });
    }

    [Fact]
    public void Display_RaisesPropertyChanged_OnChangeUndoAndSelection() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            click(vm, NumberStampTool.ToolKey, 50, 50);
            List<string?> changed = new List<string?>();
            PropertyChangedEventHandler handler = (s, e) => changed.Add(e.PropertyName);
            vm.PropertyChanged += handler;

            vm.IncreaseStampNumberCommand.Execute(null);
            Assert.Contains(nameof(EditorViewModel.CurrentStampNumberText), changed);
            changed.Clear();
            vm.UndoStack.Undo();
            Assert.Contains(nameof(EditorViewModel.CurrentStampNumberText), changed);
            changed.Clear();
            vm.SelectAnnotation(null);
            Assert.Contains(nameof(EditorViewModel.CurrentStampNumberText), changed);
            Assert.Contains(nameof(EditorViewModel.ShowStampNumberOption), changed);
            vm.PropertyChanged -= handler;
        });
    }
}
