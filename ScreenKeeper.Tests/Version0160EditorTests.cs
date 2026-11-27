using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Tools;

namespace ScreenKeeper.Tests;

/// <summary>
/// 0.16.0: Vorschau des nächsten Nummern-Stempels am Zeiger, Rechtsklick beim Werkzeug „Bereich“, blaue Monitor-Rahmen im
/// Vollbild ohne Bereich und die neuen Esc-Stufen (anderes Werkzeug → „Auswahl“ statt Schließen).
/// </summary>
public class Version0160EditorTests {
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

    private static void click(EditorViewModel vm, int x, int y) {
        vm.PointerDown(at(x, y, 1));
        vm.PointerUp(at(x, y));
    }

    private static void drag(EditorViewModel vm, int x1, int y1, int x2, int y2) {
        vm.PointerDown(at(x1, y1, 1));
        vm.PointerMove(at(x2, y2));
        vm.PointerUp(at(x2, y2));
    }

    // ---------- Stempel-Vorschau ----------

    [Fact]
    public void StampPreview_OnlyWithStampTool_AndFreePointer() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.Null(vm.GetStampPreview(at(50, 50), true));
            Assert.True(vm.SelectToolByKey(RectangleTool.ToolKey));
            Assert.Null(vm.GetStampPreview(at(50, 50), true));

            Assert.True(vm.SelectToolByKey(NumberStampTool.ToolKey));
            StampPreview? preview = vm.GetStampPreview(at(50, 60), true);
            Assert.NotNull(preview);
            Assert.Equal(new StampPreview(1, vm.Options.Color, vm.Options.StampSize, new PixelPoint(50, 60)), preview);
            // Maus nicht über der Fläche bzw. Ansicht wird verschoben
            Assert.Null(vm.GetStampPreview(at(50, 60), false));
        });
    }

    [Fact]
    public void StampPreview_CenterIsClampedLikeThePlacedStamp() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.True(vm.SelectToolByKey(NumberStampTool.ToolKey));
            Assert.Equal(new PixelPoint(0, 299), vm.GetStampPreview(at(-20, 450), true)!.Center);
            Assert.Equal(new PixelPoint(399, 0), vm.GetStampPreview(at(500, -3), true)!.Center);

            // der echte Stempel landet genau dort, wo die Vorschau stand
            StampPreview preview = vm.GetStampPreview(at(420, 150), true)!;
            click(vm, 420, 150);
            NumberStampAnnotation stamp = Assert.IsType<NumberStampAnnotation>(vm.SelectedAnnotation);
            Assert.Equal(preview.Center, new PixelPoint(stamp.X, stamp.Y));
            Assert.Equal(preview.Number, stamp.Number);
            Assert.Equal(preview.Color, stamp.Color);
            Assert.Equal(preview.Size, stamp.Size);
        });
    }

    [Fact]
    public void StampPreview_FollowsNumberColorAndSize_HiddenOverGrabbableStamp() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.True(vm.SelectToolByKey(NumberStampTool.ToolKey));
            click(vm, 100, 100);
            Assert.IsType<NumberStampAnnotation>(vm.SelectedAnnotation);

            // über dem ausgewählten Stempel gilt Verschieben: keine Vorschau
            Assert.NotEqual(EditorCursor.Cross, vm.GetCursor(at(100, 100)));
            Assert.Null(vm.GetStampPreview(at(100, 100), true));

            // daneben: nächste Nummer; Farbe und Größe wirken auf den ausgewählten Stempel und die Vorschau
            vm.SetColor(AnnotationColors.Palette[2]);
            vm.SetStampSize(48);
            StampPreview preview = vm.GetStampPreview(at(250, 200), true)!;
            Assert.Equal(2, preview.Number);
            Assert.Equal(AnnotationColors.Palette[2], preview.Color);
            Assert.Equal(48, preview.Size);

            // „+“ am ausgewählten Stempel ändert die nächste Nummer mit
            Assert.True(vm.ChangeSelectedStampNumber(4));
            Assert.Equal(6, vm.GetStampPreview(at(250, 200), true)!.Number);
        });
    }

    [Fact]
    public void StampPreview_SameDrawingAsExport() {
        Sta.Run(() => {
            NumberStampAnnotation stamp = new NumberStampAnnotation { X = 20, Y = 30, Number = 7, Color = "#FF1E88E5", Size = 32 };
            DrawingGroup group = new DrawingGroup();
            using (DrawingContext dc = group.Open()) {
                RenderService.DrawNumberStamp(dc, stamp);
            }
            Rect bounds = group.Bounds;
            Assert.False(bounds.IsEmpty);
            Assert.InRange(bounds.Left + bounds.Width / 2, 19.5, 20.5);
            Assert.InRange(bounds.Top + bounds.Height / 2, 29.5, 30.5);

            CaptureDocument document = new CaptureDocument {
                VirtualScreen = new PixelRect(0, 0, 100, 100),
                Annotations = new List<Annotation> { stamp }
            };
            DrawingGroup export = new RenderService().BuildAnnotationDrawing(document, TestImages.CreatePattern(100, 100));
            Assert.Equal(bounds, export.Bounds);
        });
    }

    // ---------- Rechtsklick ----------

    [Fact]
    public void RightClick_RegionTool_WithoutGesture_SwitchesToSelect_EvenWithSelection() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.Same(vm.SelectionTool, vm.ActiveTool);
            Assert.Equal(RightClickResult.SwitchedToSelect, vm.RightClick(false));
            Assert.Same(vm.ObjectSelectTool, vm.ActiveTool);

            Assert.True(vm.SelectToolByKey(SelectionTool.ToolKey));
            vm.SetSelection(new PixelRect(10, 10, 100, 80));
            int steps = vm.UndoStack.UndoCount;
            Assert.Equal(RightClickResult.SwitchedToSelect, vm.RightClick(false));
            Assert.Same(vm.ObjectSelectTool, vm.ActiveTool);
            Assert.Equal(new PixelRect(10, 10, 100, 80), vm.Selection);
            Assert.Equal(steps, vm.UndoStack.UndoCount);
        });
    }

    [Fact]
    public void RightClick_RegionTool_DuringGesture_OnlyCancels() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            vm.PointerDown(at(10, 10, 1));
            vm.PointerMove(at(120, 90));
            Assert.True(vm.IsGestureActive);
            Assert.NotNull(vm.PreviewSelection);
            Assert.Equal(RightClickResult.Cancelled, vm.RightClick(true));
            Assert.False(vm.IsGestureActive);
            Assert.Null(vm.PreviewSelection);
            Assert.Null(vm.Selection);
            Assert.Same(vm.SelectionTool, vm.ActiveTool);
            // das Loslassen der linken Taste danach setzt keinen Bereich
            vm.PointerUp(at(120, 90));
            Assert.Null(vm.Selection);

            // laufende Tastatur-Auswahl ebenso (auch ohne Mausfang)
            vm.KeyboardMark();
            Assert.True(vm.IsKeyboardSelecting);
            Assert.Equal(RightClickResult.Cancelled, vm.RightClick(false));
            Assert.False(vm.IsKeyboardSelecting);
            Assert.Same(vm.SelectionTool, vm.ActiveTool);

            // nur Mausfang (Klick ohne Bewegung) zählt auch als Vorgang
            Assert.Equal(RightClickResult.Cancelled, vm.RightClick(true));
            Assert.Same(vm.SelectionTool, vm.ActiveTool);
        });
    }

    [Fact]
    public void RightClick_OtherTools_DoesNothing() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            foreach(string key in new[] { ObjectSelectTool.ToolKey, RectangleTool.ToolKey, NumberStampTool.ToolKey, TextSelectTool.ToolKey }) {
                Assert.True(vm.SelectToolByKey(key));
                IEditorTool tool = vm.ActiveTool;
                Assert.Equal(RightClickResult.None, vm.RightClick(false));
                Assert.Same(tool, vm.ActiveTool);
            }
            // auch nicht während eines Vorgangs
            Assert.True(vm.SelectToolByKey(RectangleTool.ToolKey));
            vm.PointerDown(at(10, 10, 1));
            vm.PointerMove(at(60, 60));
            Assert.Equal(RightClickResult.None, vm.RightClick(true));
            Assert.True(vm.IsGestureActive);
            vm.PointerUp(at(60, 60));
            Assert.IsType<RectangleAnnotation>(vm.SelectedAnnotation);
        });
    }

    [Fact]
    public void RegionTooltip_MentionsRightClick() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.Contains("Rechtsklick", vm.SelectionTool.ToolTip);
        });
    }

    // ---------- Monitor-Rahmen ----------

    [Fact]
    public void MonitorFrame_ThreeDevicePixelsInside_NoOverlap() {
        List<Rect> bars = MonitorFramePlacement.GetBars(new[] { new Rect(0, 0, 1920, 1080) }, 1);
        Assert.Equal(new[] {
            new Rect(0, 0, 1920, 3),
            new Rect(0, 1077, 1920, 3),
            new Rect(0, 3, 3, 1074),
            new Rect(1917, 3, 3, 1074)
        }, bars);
        // zusammen genau der Rand: Fläche = Umfang-Band, keine Überlappung
        double area = bars.Sum(b => b.Width * b.Height);
        Assert.Equal(1920 * 1080 - 1914 * 1074, area);
        for(int i = 0; i < bars.Count; i++) {
            for(int j = i + 1; j < bars.Count; j++) {
                Rect overlap = Rect.Intersect(bars[i], bars[j]);
                Assert.True(overlap.IsEmpty || overlap.Width * overlap.Height == 0);
            }
        }
    }

    [Fact]
    public void MonitorFrame_ScaledDisplay_ThicknessIsThreeDevicePixels() {
        // 150 %: Monitor mit 1920×1080 Gerätepixeln = 1280×720 DIP, Rahmen 2 DIP = 3 Gerätepixel
        List<Rect> bars = MonitorFramePlacement.GetBars(new[] { new Rect(1280, 0, 1280, 720) }, 1.5);
        Assert.Equal(4, bars.Count);
        Assert.Equal(new Rect(1280, 0, 1280, 2), bars[0]);
        Assert.Equal(new Rect(1280, 718, 1280, 2), bars[1]);
        Assert.Equal(2, bars[2].Width, 9);
        Assert.Equal(2560 - 2, bars[3].Left, 9);
        foreach(Rect bar in bars) {
            // jede Kante liegt auf einem Gerätepixel
            Assert.Equal(Math.Round(bar.Left * 1.5), bar.Left * 1.5, 9);
            Assert.Equal(Math.Round(bar.Bottom * 1.5), bar.Bottom * 1.5, 9);
        }
    }

    [Fact]
    public void MonitorFrame_RoundsToDevicePixels_PerMonitor_TinyMonitorFilled() {
        List<Rect> bars = MonitorFramePlacement.GetBars(new[] { new Rect(0.3, 0.2, 99.6, 50) }, 1);
        Assert.Equal(new Rect(0, 0, 100, 3), bars[0]);
        Assert.Equal(new Rect(97, 3, 3, 44), bars[3]);

        List<Rect> two = MonitorFramePlacement.GetBars(new[] { new Rect(0, 0, 1920, 1080), new Rect(1920, 0, 1280, 1024) }, 1);
        Assert.Equal(8, two.Count);
        Assert.Equal(new Rect(1920, 0, 1280, 3), two[4]);
        Assert.Equal(new Rect(3200 - 3, 3, 3, 1018), two[7]);

        Assert.Equal(new[] { new Rect(10, 10, 5, 40) }, MonitorFramePlacement.GetBars(new[] { new Rect(10, 10, 5, 40) }, 1));
        Assert.Empty(MonitorFramePlacement.GetBars(new[] { Rect.Empty, new Rect(5, 5, 0, 10) }, 1));
    }

    // ---------- Esc-Stufen ----------

    [Fact]
    public void Escape_DrawingTool_SwitchesToSelect_ThenCloses() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.True(vm.SelectToolByKey(RectangleTool.ToolKey));
            drag(vm, 10, 10, 80, 60);
            Annotation rectangle = Assert.IsType<RectangleAnnotation>(vm.SelectedAnnotation);

            Assert.Equal(EscapeResult.SwitchedToSelect, vm.Escape());
            Assert.Same(vm.ObjectSelectTool, vm.ActiveTool);
            // das gerade gezeichnete Objekt bleibt ausgewählt
            Assert.Same(rectangle, vm.SelectedAnnotation);
            // das zweite Esc schließt, ohne erst die Auswahl aufzuheben
            Assert.Equal(EscapeResult.Close, vm.Escape());
            Assert.Same(rectangle, vm.SelectedAnnotation);
            Assert.Single(vm.Document.Annotations);
        });
    }

    [Fact]
    public void Escape_RegionAndSelectTool_CloseAsBefore() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.Equal(EscapeResult.Close, vm.Escape());
            vm.SetSelection(new PixelRect(5, 5, 50, 50));
            Assert.Equal(EscapeResult.Close, vm.Escape());
            Assert.True(vm.SelectToolByKey(ObjectSelectTool.ToolKey));
            Assert.Equal(EscapeResult.Close, vm.Escape());
            Assert.Same(vm.ObjectSelectTool, vm.ActiveTool);
        });
    }

    [Fact]
    public void Escape_CancelsRunningGestureFirst() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            vm.PointerDown(at(10, 10, 1));
            vm.PointerMove(at(120, 90));
            Assert.Equal(EscapeResult.Cancelled, vm.Escape());
            Assert.Null(vm.PreviewSelection);
            Assert.Same(vm.SelectionTool, vm.ActiveTool);

            Assert.True(vm.SelectToolByKey(EllipseTool.ToolKey));
            vm.PointerDown(at(10, 10, 1));
            vm.PointerMove(at(60, 60));
            Assert.Equal(EscapeResult.Cancelled, vm.Escape());
            Assert.Same(vm.FindTool(EllipseTool.ToolKey), vm.ActiveTool);
            Assert.Empty(vm.Document.Annotations);
            Assert.Equal(EscapeResult.SwitchedToSelect, vm.Escape());
            Assert.Equal(EscapeResult.Close, vm.Escape());
        });
    }

    [Fact]
    public void Escape_TextSelect_ClearsMarking_ThenSelect_ThenClose() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            RecognizedWord word = new RecognizedWord { Text = "Hallo", Bounds = new PixelRect(10, 10, 30, 10) };
            RecognizedText text = new RecognizedText {
                Region = new PixelRect(0, 0, 400, 300),
                Language = "de-DE",
                Lines = new List<RecognizedLine> { new RecognizedLine { Text = "Hallo", Words = new List<RecognizedWord> { word }, Bounds = word.Bounds } }
            };
            Assert.True(vm.ApplyOcrResult(text));
            Assert.True(vm.SelectToolByKey(TextSelectTool.ToolKey));
            Assert.True(vm.TextSelectTool.SelectAll());

            Assert.Equal(EscapeResult.TextSelectionCleared, vm.Escape());
            Assert.False(vm.TextSelectTool.HasSelection);
            Assert.Same(vm.TextSelectTool, vm.ActiveTool);
            Assert.Equal(EscapeResult.SwitchedToSelect, vm.Escape());
            Assert.Equal(EscapeResult.Close, vm.Escape());
        });
    }

    [Fact]
    public void EscapeInTextField_KeepsText_AndSwitchesToSelect() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.True(vm.SelectToolByKey(TextTool.ToolKey));
            click(vm, 50, 50);
            Assert.True(vm.IsTextEditing);
            vm.UpdateEditingText("behalten");

            vm.CommitTextEditAndSelect();
            Assert.False(vm.IsTextEditing);
            TextAnnotation text = Assert.IsType<TextAnnotation>(Assert.Single(vm.Document.Annotations));
            Assert.Equal("behalten", text.Text);
            Assert.Same(vm.ObjectSelectTool, vm.ActiveTool);
            // der übernommene Text ist ausgewählt; das nächste Esc schließt trotzdem
            Assert.True(vm.HasSelectedAnnotation);
            Assert.Equal(EscapeResult.Close, vm.Escape());
        });
    }

    [Fact]
    public void StampTool_PlusMinus_ChangesNextNumber_BeforeFirstStamp() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.True(vm.SelectToolByKey(NumberStampTool.ToolKey));
            Assert.True(vm.CanChangeNextStampNumber);
            Assert.False(vm.CanChangeStampNumber);
            Assert.Equal(1, vm.NextStampNumber);

            // unter 1 geht es nicht
            Assert.True(vm.ChangeStampNumber(-1));
            Assert.Equal(1, vm.NextStampNumber);
            Assert.True(vm.ChangeStampNumber(1));
            Assert.True(vm.ChangeStampNumber(1));
            Assert.Equal(3, vm.NextStampNumber);
            Assert.Equal("3", vm.CurrentStampNumberText);
            Assert.Empty(vm.Document.Annotations);

            // der Stempel bekommt die gewählte Nummer, danach geht es mit der folgenden weiter
            click(vm, 50, 50);
            NumberStampAnnotation stamp = Assert.IsType<NumberStampAnnotation>(Assert.Single(vm.Document.Annotations));
            Assert.Equal(3, stamp.Number);
            Assert.Equal(4, vm.NextStampNumber);
        });
    }

    [Fact]
    public void StampTool_NextNumber_ContinuesAfterLastStamp_AlsoBelowHighest() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.True(vm.SelectToolByKey(NumberStampTool.ToolKey));
            for(int i = 0; i < 4; i++) Assert.True(vm.ChangeStampNumber(1));
            click(vm, 20, 20);
            Assert.Equal(6, vm.NextStampNumber);

            // gerade gesetzter Stempel ist ausgewählt: −/+ ändern ihn, die nächste Nummer folgt
            Assert.True(vm.CanChangeStampNumber);
            Assert.False(vm.CanChangeNextStampNumber);
            Assert.True(vm.ChangeStampNumber(-1));
            Assert.Equal(4, vm.Document.Annotations.OfType<NumberStampAnnotation>().Single().Number);
            Assert.Equal(5, vm.NextStampNumber);

            // unterhalb der größten Nummer weiterzählen: 5 gesetzt, dann 2 gewählt → 2, danach 3
            click(vm, 80, 80);
            vm.SelectAnnotation(null);
            Assert.True(vm.CanChangeNextStampNumber);
            for(int i = 0; i < 4; i++) Assert.True(vm.ChangeStampNumber(-1));
            Assert.Equal(2, vm.NextStampNumber);
            click(vm, 140, 80);
            Assert.Equal(new[] { 4, 5, 2 }, vm.Document.Annotations.OfType<NumberStampAnnotation>().Select(s => s.Number).ToArray());
            Assert.Equal(3, vm.NextStampNumber);
        });
    }

    [Fact]
    public void StampHint_BelowPreview_AboveAtBottomEdge_ClampedSideways() {
        Rect bounds = new Rect(0, 0, 1000, 800);
        Size hint = new Size(80, 20);
        // mittig unter dem Stempel, mit Abstand
        Assert.Equal(new Point(460, 426), StampHintPlacement.Place(new Point(500, 400), 20, hint, bounds, 6));
        // unten kein Platz: über dem Stempel
        Assert.Equal(new Point(460, 734), StampHintPlacement.Place(new Point(500, 780), 20, hint, bounds, 6));
        // am linken und rechten Rand bleibt der Hinweis im Monitor
        Assert.Equal(0, StampHintPlacement.Place(new Point(10, 400), 20, hint, bounds, 6).X);
        Assert.Equal(920, StampHintPlacement.Place(new Point(995, 400), 20, hint, bounds, 6).X);
    }

    [Fact]
    public void StampHint_AtPreviewFirst_ThenAtSelectedStamp() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.Null(vm.GetStampHintTarget(null));
            Assert.True(vm.SelectToolByKey(NumberStampTool.ToolKey));
            StampPreview preview = new StampPreview(1, vm.Options.Color, vm.Options.StampSize, new PixelPoint(30, 40));
            // nichts gesetzt: Hinweis an der Vorschau, ohne sichtbare Vorschau keiner
            // an der Vorschau mit Nummer
            Assert.Equal(new StampHintTarget(30, 40, vm.Options.StampSize, 1), vm.GetStampHintTarget(preview));
            Assert.Null(vm.GetStampHintTarget(null));

            // gesetzt: der neue Stempel ist ausgewählt, die Tasten ändern ihn – der Hinweis steht bei ihm
            click(vm, 50, 50);
            StampPreview next = preview with { Number = 2, Center = new PixelPoint(120, 90) };
            Assert.Equal(new StampHintTarget(50, 50, vm.Options.StampSize), vm.GetStampHintTarget(next));
            Assert.Equal(new StampHintTarget(50, 50, vm.Options.StampSize), vm.GetStampHintTarget(null));

            // anderes Werkzeug ohne ausgewählten Stempel: kein Hinweis
            Assert.True(vm.SelectToolByKey(RectangleTool.ToolKey));
            Assert.Null(vm.GetStampHintTarget(null));
        });
    }

    [Fact]
    public void OtherTool_PlusMinus_DoesNotChangeNextNumber() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.False(vm.CanChangeNextStampNumber);
            Assert.False(vm.ChangeStampNumber(1));
            Assert.Equal(1, vm.NextStampNumber);
        });
    }
}
