using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Tools;
using Xunit.Abstractions;

namespace ScreenKeeper.Tests;

/// <summary>
/// 0.14.0: Werkzeugleiste ohne „Wackler“ – Knöpfe werden höchstens ausgegraut, die Optionen liegen in einem Platz fester
/// Breite neben „Objekt löschen“. Je Objekttyp sind die Optionen immer dieselben (−/+ am Stempel und der Augen-Knopf der
/// Maus sind ohne Auswahl ausgegraut) und Wertanzeigen fest breit – Leiste, Lage und alle Knöpfe außerhalb des Platzes
/// bleiben also gleich; innerhalb wandert beim Ändern eines Werts nichts.
/// </summary>
public class ToolbarLayoutTests {
    private readonly ITestOutputHelper output;

    public ToolbarLayoutTests(ITestOutputHelper output) {
        this.output = output;
    }

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
        Assert.True(vm.SelectToolByKey(tool));
        vm.PointerDown(at(x, y, 1));
        vm.PointerUp(at(x, y));
    }

    private static void drag(EditorViewModel vm, string tool, int x1, int y1, int x2, int y2) {
        Assert.True(vm.SelectToolByKey(tool));
        vm.PointerDown(at(x1, y1, 1));
        vm.PointerMove(at(x2, y2));
        vm.PointerUp(at(x2, y2));
    }

    /// <summary>Messen wie im Editor (unbegrenzt), nachdem verzögerte Bindungen und Elemente erzeugt sind.</summary>
    private static Size measure(FrameworkElement element) {
        DispatcherFrame frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return element.DesiredSize;
    }

    /// <summary>Lage aller Knöpfe außerhalb des Optionsplatzes (nach Anordnen der Leiste).</summary>
    private static List<Point> buttonPositions(FrameworkElement toolbar, FrameworkElement slot) {
        toolbar.Arrange(new Rect(toolbar.DesiredSize));
        toolbar.UpdateLayout();
        List<Point> result = new List<Point>();
        void collect(DependencyObject element) {
            if(element == slot) return;
            if(element is ButtonBase button) result.Add(button.TranslatePoint(new Point(0, 0), toolbar));
            for(int i = 0; i < VisualTreeHelper.GetChildrenCount(element); i++) collect(VisualTreeHelper.GetChild(element, i));
        }
        collect(toolbar);
        return result;
    }

    /// <summary>Breite der gerade sichtbaren Optionen (hängt eine davon vom Wert ab, wandern die folgenden Knöpfe).</summary>
    private static double optionsWidth(FrameworkElement toolbar) {
        measure(toolbar);
        return ((FrameworkElement)toolbar.FindName("options")).DesiredSize.Width;
    }

    [Fact]
    public void Commands_DisabledWithoutMatchingSelection() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.False(vm.DeleteSelectedCommand.CanExecute(null));

            drag(vm, RectangleTool.ToolKey, 50, 50, 120, 100);
            Assert.True(vm.HasSelectedAnnotation);
            Assert.True(vm.DeleteSelectedCommand.CanExecute(null));
            Assert.False(vm.IncreaseStampNumberCommand.CanExecute(null));
            Assert.False(vm.ToggleCursorVisibleCommand.CanExecute(null));

            vm.SelectAnnotation(null);
            Assert.False(vm.DeleteSelectedCommand.CanExecute(null));

            click(vm, ObjectSelectTool.ToolKey, 50, 75);
            Assert.True(vm.DeleteSelectedCommand.CanExecute(null));
            vm.DeleteSelectedCommand.Execute(null);
            Assert.Empty(vm.Document.Annotations);
            Assert.False(vm.DeleteSelectedCommand.CanExecute(null));

            // virtuelle Maus: Augen-Knopf beim Werkzeug sichtbar, aber erst bei ausgewählter Maus wirksam
            Assert.True(vm.SelectToolByKey(CursorTool.ToolKey));
            Assert.True(vm.ShowCursorVisibleOption);
            Assert.False(vm.ToggleCursorVisibleCommand.CanExecute(null));
            vm.Document.Annotations.Add(new CursorAnnotation { X = 300, Y = 250 });
            click(vm, ObjectSelectTool.ToolKey, 302, 253);
            Assert.True(vm.ShowCursorVisibleOption);
            Assert.True(vm.ToggleCursorVisibleCommand.CanExecute(null));
        });
    }

    [Fact]
    public void ButtonsOutsideSlot_AndPlacement_IdenticalForAllToolsSelectionsAndValues() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Views.EditorToolbar toolbar = new Views.EditorToolbar { DataContext = vm };
            FrameworkElement slot = (FrameworkElement)toolbar.FindName("optionsSlot");
            FrameworkElement options = (FrameworkElement)toolbar.FindName("options");
            Size expectedSize = measure(toolbar);
            List<Point> expectedButtons = buttonPositions(toolbar, slot);
            Rect monitor = new Rect(0, 0, 1920, 1080);
            Rect selection = new Rect(700, 300, 300, 200);
            Point expectedPosition = ToolbarPlacement.Place(selection, expectedSize, monitor, 10);
            output.WriteLine($"Leiste {expectedSize.Width} × {expectedSize.Height}, Optionsplatz {slot.DesiredSize.Width}, {expectedButtons.Count} feste Knöpfe");
            Assert.Equal(27, expectedButtons.Count);

            double widestOptions = 0;
            string widestState = "";
            void check(string state) {
                Size size = measure(toolbar);
                Assert.True(expectedSize == size, $"{state}: Leiste {size} statt {expectedSize}");
                Assert.Equal(expectedButtons, buttonPositions(toolbar, slot));
                Assert.True(options.DesiredSize.Width <= Views.EditorToolbar.OptionsSlotWidth,
                    $"{state}: Optionen {options.DesiredSize.Width} breiter als ihr Platz {Views.EditorToolbar.OptionsSlotWidth}");
                Assert.Equal(expectedPosition, ToolbarPlacement.Place(selection, size, monitor, 10));
                output.WriteLine($"{state}: Optionen {options.DesiredSize.Width}");
                if(options.DesiredSize.Width > widestOptions) {
                    widestOptions = options.DesiredSize.Width;
                    widestState = state;
                }
            }

            check("ohne Werkzeug");
            foreach(ToolItem item in vm.Tools) {
                vm.SelectTool(item.Tool);
                check($"Werkzeug {item.Tool.Key}");
            }

            // Text: Bearbeitung offen, automatische und feste Größe
            click(vm, TextTool.ToolKey, 60, 200);
            Assert.True(vm.IsTextEditing);
            vm.UpdateEditingText("Test");
            check("Text in Bearbeitung");
            vm.SetFontSize(12);
            check("Text 12 px");
            vm.SetAutoFontSize();
            check("Text Auto");
            vm.CommitTextEdit();

            // ausgewählte Objekte
            drag(vm, RectangleTool.ToolKey, 20, 20, 80, 60);
            check("Rechteck ausgewählt");
            drag(vm, FreehandTool.ToolKey, 100, 20, 160, 60);
            check("Freihand ausgewählt");
            click(vm, NumberStampTool.ToolKey, 200, 50);
            check("Stempel ausgewählt");
            vm.Document.Annotations.Add(new CursorAnnotation { X = 300, Y = 250 });
            click(vm, ObjectSelectTool.ToolKey, 302, 253);
            Assert.IsType<CursorAnnotation>(vm.SelectedAnnotation);
            check("Maus ausgewählt");
            vm.DeleteSelectedCommand.Execute(null);
            check("nach Objekt löschen");
            vm.Undo();
            check("nach Rückgängig");
            vm.SelectAnnotation(null);
            check("keine Auswahl");
            output.WriteLine($"breiteste Optionen: {widestOptions} ({widestState}), Platz {Views.EditorToolbar.OptionsSlotWidth}");
        });
    }

    [Fact]
    public void StampOptions_WidthStable_ToolSelectionAndNumbers() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Views.EditorToolbar toolbar = new Views.EditorToolbar { DataContext = vm };
            Assert.True(vm.SelectToolByKey(NumberStampTool.ToolKey));
            double width = optionsWidth(toolbar);
            Assert.False(vm.IncreaseStampNumberCommand.CanExecute(null));
            Assert.False(vm.DecreaseStampNumberCommand.CanExecute(null));

            // stempeln (der neue Stempel ist ausgewählt), dann Nummer 1 → 10 → 100 → 999 und Stempelgrößen
            click(vm, NumberStampTool.ToolKey, 50, 50);
            Assert.True(vm.IncreaseStampNumberCommand.CanExecute(null));
            Assert.Equal(width, optionsWidth(toolbar));
            foreach(int delta in new[] { 9, 90, 899 }) {
                Assert.True(vm.ChangeSelectedStampNumber(delta));
                Assert.Equal(width, optionsWidth(toolbar));
            }
            Assert.Equal("999", vm.CurrentStampNumberText);
            foreach(double size in ToolOptions.StampSizes) {
                vm.SetStampSize(size);
                Assert.Equal(width, optionsWidth(toolbar));
            }

            // Auswahl aufheben (Werkzeug bleibt): gleiche Breite, −/+ wieder ausgegraut, Anzeige = nächste Nummer
            Assert.True(vm.ChangeSelectedStampNumber(-1));
            vm.SelectAnnotation(null);
            Assert.Equal(width, optionsWidth(toolbar));
            Assert.False(vm.IncreaseStampNumberCommand.CanExecute(null));
            Assert.Equal("999", vm.CurrentStampNumberText);
        });
    }

    [Fact]
    public void Options_WidthStable_WhenValuesChange() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Views.EditorToolbar toolbar = new Views.EditorToolbar { DataContext = vm };
            double width() => optionsWidth(toolbar);

            Assert.True(vm.SelectToolByKey(RectangleTool.ToolKey));
            double rectangleWidth = width();
            foreach(double thickness in ToolOptions.Thicknesses) {
                vm.SetThickness(thickness);
                Assert.Equal(rectangleWidth, width());
            }

            Assert.True(vm.SelectToolByKey(PixelateTool.ToolKey));
            double pixelateWidth = width();
            foreach(int blockSize in ToolOptions.BlockSizes) {
                vm.SetBlockSize(blockSize);
                Assert.Equal(pixelateWidth, width());
            }

            Assert.True(vm.SelectToolByKey(BlurTool.ToolKey));
            double blurWidth = width();
            foreach(int radius in ToolOptions.BlurRadii) {
                vm.SetBlurRadius(radius);
                Assert.Equal(blurWidth, width());
            }

            Assert.True(vm.SelectToolByKey(TextTool.ToolKey));
            double textWidth = width();
            vm.SetFontSize(12);
            Assert.Equal(textWidth, width());
            vm.SetFontSize(400);
            Assert.Equal(textWidth, width());
            vm.SetAutoFontSize();
            Assert.Equal(textWidth, width());

            Assert.True(vm.SelectToolByKey(CursorTool.ToolKey));
            double cursorWidth = width();
            foreach(string kind in new[] { CursorKinds.Arrow, CursorKinds.Hand, CursorKinds.IBeam }) {
                vm.SetCursorKind(kind);
                Assert.Equal(cursorWidth, width());
            }
        });
    }

    [Fact]
    public void Placement_WideToolbar_StaysInsideMonitor() {
        Rect monitor = new Rect(0, 0, 1920, 1080);
        Size size = new Size(1280, 44);
        Point below = ToolbarPlacement.Place(new Rect(500, 300, 400, 100), size, monitor, 10);
        Assert.Equal(new Point(60, 410), below);
        Point above = ToolbarPlacement.Place(new Rect(500, 1000, 400, 60), size, monitor, 10);
        Assert.Equal(new Point(60, 1000 - 10 - 44), above);
        // rechter Rand: in den Monitor geschoben
        Point clamped = ToolbarPlacement.Place(new Rect(1900, 100, 20, 20), size, monitor, 10);
        Assert.Equal(1920 - 1280, clamped.X);
    }
}
