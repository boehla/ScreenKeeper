using System.Windows.Input;
using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Tools;

namespace ScreenKeeper.Tests;

/// <summary>
/// Editor-Host ohne Fenster.
/// </summary>
public sealed class TestEditorHost : IEditorHost {
    public PixelPoint GetCursorImagePoint() {
        return new PixelPoint(0, 0);
    }

    public string? AskSaveAsPath(string initialFolder, string fileName) {
        return null;
    }

    public void ShowError(string message) {
    }

    public void CloseEditor() {
    }

    public void DiscardCapture() {
    }
}

/// <summary>
/// Zeichenwerkzeuge, Objektauswahl, Textbearbeitung und Optionen über das <see cref="EditorViewModel"/> –
/// mit denselben Maus-Sequenzen, die das Fenster erzeugt.
/// </summary>
public class EditorToolTests {
    private static EditorViewModel create(CaptureDocument? document = null) {
        CaptureDocument doc = document ?? new CaptureDocument {
            VirtualScreen = new PixelRect(0, 0, 400, 300),
            Monitors = new List<MonitorInfo> { new MonitorInfo { Bounds = new PixelRect(0, 0, 400, 300), Primary = true } }
        };
        return new EditorViewModel(doc, TestImages.CreatePattern(400, 300), new TestEditorHost(), new AppSettings());
    }

    private static PointerInput at(int x, int y, ModifierKeys modifiers = ModifierKeys.None, int clicks = 0) {
        return PointerInput.AtPixel(new PixelPoint(x, y), modifiers) with { ClickCount = clicks };
    }

    private static void drag(EditorViewModel vm, string tool, int x1, int y1, int x2, int y2, ModifierKeys modifiers = ModifierKeys.None) {
        if(tool.Length > 0) Assert.True(vm.SelectToolByKey(tool));
        vm.PointerDown(at(x1, y1, modifiers, 1));
        for(int i = 1; i <= 10; i++) vm.PointerMove(at(x1 + (x2 - x1) * i / 10, y1 + (y2 - y1) * i / 10, modifiers));
        vm.PointerUp(at(x2, y2, modifiers));
    }

    private static void click(EditorViewModel vm, string tool, int x, int y, int clicks = 1) {
        if(tool.Length > 0) Assert.True(vm.SelectToolByKey(tool));
        vm.PointerDown(at(x, y, ModifierKeys.None, clicks));
        vm.PointerUp(at(x, y));
    }

    private static T single<T>(EditorViewModel vm) where T : Annotation {
        return Assert.Single(vm.Document.Annotations.OfType<T>());
    }

    [Fact]
    public void AllTools_AreRegistered_WithShortcuts() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.Equal(13, vm.Tools.Count);
            Assert.Same(vm.SelectionTool, vm.Tools[0].Tool);
            foreach(Key key in new[] { Key.S, Key.R, Key.E, Key.L, Key.A, Key.F, Key.T, Key.H, Key.P, Key.B, Key.N, Key.M }) {
                string? toolKey = ToolShortcuts.GetToolKey(key);
                Assert.NotNull(toolKey);
                Assert.NotNull(vm.FindTool(toolKey!));
            }
            Assert.Null(ToolShortcuts.GetToolKey(Key.Z));
            Assert.Equal(12, vm.Tools.Select(t => t.Tool.Key).Count(k => k != SelectionTool.ToolKey));
        });
    }

    [Fact]
    public void Rectangle_Drag_CreatesOneUndoStep_WithOptions() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            vm.Options.Color = "#FF112233";
            vm.Options.Thickness = 6;
            int changes = 0;
            vm.DocumentChanged += (s, e) => changes++;
            drag(vm, RectangleTool.ToolKey, 10, 10, 50, 40);
            RectangleAnnotation rectangle = single<RectangleAnnotation>(vm);
            Assert.Equal(new PixelRect(10, 10, 41, 31), rectangle.Rect);
            Assert.Equal("#FF112233", rectangle.Stroke);
            Assert.Equal(6, rectangle.Thickness);
            Assert.Null(rectangle.Fill);
            Assert.Equal(1, vm.UndoStack.UndoCount);
            Assert.True(changes >= 10, "Live-Vorschau beim Ziehen");
            vm.Undo();
            Assert.Empty(vm.Document.Annotations);
            vm.Redo();
            Assert.Equal(new PixelRect(10, 10, 41, 31), single<RectangleAnnotation>(vm).Rect);
        });
    }

    [Fact]
    public void Shapes_BelowMinimumSize_AreDiscarded() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            drag(vm, RectangleTool.ToolKey, 10, 10, 11, 30);
            drag(vm, EllipseTool.ToolKey, 10, 10, 30, 11);
            drag(vm, LineTool.ToolKey, 10, 10, 11, 11);
            click(vm, PixelateTool.ToolKey, 10, 10);
            Assert.Empty(vm.Document.Annotations);
            Assert.Equal(0, vm.UndoStack.UndoCount);
        });
    }

    [Fact]
    public void Cancel_DuringDrag_RemovesPreview() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            vm.SelectToolByKey(BlurTool.ToolKey);
            vm.PointerDown(at(10, 10, ModifierKeys.None, 1));
            vm.PointerMove(at(60, 60));
            Assert.Single(vm.Document.Annotations);
            Assert.True(vm.IsGestureActive);
            vm.CancelGesture();
            vm.PointerUp(at(60, 60));
            Assert.Empty(vm.Document.Annotations);
            Assert.Equal(0, vm.UndoStack.UndoCount);
        });
    }

    [Fact]
    public void ArrowAndLine_ShiftSnapsTo45Degrees() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            drag(vm, ArrowTool.ToolKey, 100, 100, 160, 108, ModifierKeys.Shift);
            Assert.Equal(new[] { new PixelPoint(100, 100), new PixelPoint(160, 100) }, single<ArrowAnnotation>(vm).Points);
            drag(vm, LineTool.ToolKey, 100, 100, 140, 144, ModifierKeys.Shift);
            Assert.Equal(new[] { new PixelPoint(100, 100), new PixelPoint(142, 142) }, single<LineAnnotation>(vm).Points);
        });
    }

    [Fact]
    public void Freehand_CollectsAndThinsPoints() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            vm.SelectToolByKey(FreehandTool.ToolKey);
            vm.PointerDown(at(10, 10, ModifierKeys.None, 1));
            for(int x = 11; x <= 60; x++) vm.PointerMove(at(x, 10));
            for(int y = 11; y <= 40; y++) vm.PointerMove(at(60, y));
            vm.PointerUp(at(60, 40));
            FreehandAnnotation freehand = single<FreehandAnnotation>(vm);
            Assert.Equal(new[] { new PixelPoint(10, 10), new PixelPoint(60, 10), new PixelPoint(60, 40) }, freehand.Points);
            Assert.True(freehand.Smooth);
        });
    }

    [Fact]
    public void NumberStamps_CountUp_AndAreNotRenumbered() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            click(vm, NumberStampTool.ToolKey, 50, 50);
            click(vm, "", 100, 50);
            click(vm, "", 150, 50);
            Assert.Equal(new[] { 1, 2, 3 }, vm.Document.Annotations.OfType<NumberStampAnnotation>().Select(s => s.Number));
            click(vm, ObjectSelectTool.ToolKey, 100, 50);
            Assert.Equal(2, ((NumberStampAnnotation)vm.SelectedAnnotation!).Number);
            vm.DeleteSelectedAnnotation();
            Assert.Equal(new[] { 1, 3 }, vm.Document.Annotations.OfType<NumberStampAnnotation>().Select(s => s.Number));
            click(vm, NumberStampTool.ToolKey, 200, 50);
            Assert.Equal(new[] { 1, 3, 4 }, vm.Document.Annotations.OfType<NumberStampAnnotation>().Select(s => s.Number));
            Assert.Equal(4, NumberStampTool.GetNextNumber(new Annotation[] { new NumberStampAnnotation { Number = 3 }, new RectangleAnnotation() }));
            Assert.Equal(1, NumberStampTool.GetNextNumber(Array.Empty<Annotation>()));
        });
    }

    [Fact]
    public void CursorTool_UsesKind_CapturedOnlyWithImage() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.DoesNotContain(vm.CursorKindChoices, c => (string)c.Value == CursorKinds.Captured);
            vm.Options.CursorKind = CursorKinds.Captured;
            click(vm, CursorTool.ToolKey, 30, 40);
            CursorAnnotation cursor = single<CursorAnnotation>(vm);
            Assert.Equal((30, 40, CursorKinds.Arrow, true), (cursor.X, cursor.Y, cursor.Kind, cursor.Visible));
            vm.SetCursorKind(CursorKinds.IBeam);
            click(vm, "", 80, 40);
            Assert.Equal(CursorKinds.IBeam, vm.Document.Annotations.OfType<CursorAnnotation>().Last().Kind);

            CaptureDocument withCursor = new CaptureDocument { Cursor = new CursorInfo { Png = "AAAA" } };
            EditorViewModel captured = create(withCursor);
            Assert.Contains(captured.CursorKindChoices, c => (string)c.Value == CursorKinds.Captured);
        });
    }

    [Fact]
    public void ObjectSelect_PicksTopmost_MovesDeletesAndUndoes() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            drag(vm, HighlightTool.ToolKey, 10, 10, 100, 100);
            drag(vm, "", 50, 50, 150, 150);
            string bottomId = vm.Document.Annotations[0].Id;
            string topId = vm.Document.Annotations[1].Id;
            click(vm, ObjectSelectTool.ToolKey, 70, 70);
            Assert.Equal(topId, vm.SelectedAnnotationId);
            Assert.NotNull(vm.GetSelectionVisual());
            Assert.Equal(8, vm.GetSelectionVisual()!.Handles.Count);

            drag(vm, "", 70, 70, 75, 80);
            Assert.Equal(new PixelRect(55, 60, 101, 101), ((HighlightAnnotation)vm.Document.Annotations[1]).Rect);
            Assert.Equal(3, vm.UndoStack.UndoCount);
            Assert.Equal(Strings.UndoMoveAnnotation, vm.UndoStack.UndoDescription);

            // Anfasser rechts unten (Kante bei 156/161) um 10 nach rechts
            drag(vm, "", 156, 161, 166, 161);
            Assert.Equal(new PixelRect(55, 60, 111, 101), ((HighlightAnnotation)vm.Document.Annotations[1]).Rect);
            Assert.Equal(Strings.UndoResizeAnnotation, vm.UndoStack.UndoDescription);

            vm.DeleteSelectedAnnotation();
            Assert.Single(vm.Document.Annotations);
            Assert.Null(vm.SelectedAnnotation);
            vm.Undo();
            Assert.Equal(new[] { bottomId, topId }, vm.Document.Annotations.Select(a => a.Id));
            vm.Undo();
            vm.Undo();
            Assert.Equal(new PixelRect(50, 50, 101, 101), ((HighlightAnnotation)vm.Document.Annotations[1]).Rect);

            // Klick ins Leere hebt die Auswahl auf
            click(vm, "", 300, 250);
            Assert.Null(vm.SelectedAnnotationId);
        });
    }

    [Fact]
    public void RegionTool_ClickOnObjectSelectsIt_EmptySpaceStillCreatesRegion() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            drag(vm, RectangleTool.ToolKey, 20, 20, 80, 60);
            vm.SelectTool(vm.SelectionTool);
            // Das gezeichnete Objekt ist ausgewählt; hier soll der Klick auf ein nicht ausgewähltes Objekt geprüft werden
            vm.SelectAnnotation(null);
            // Rahmen links treffen und ziehen → Objekt verschoben, kein Bereich
            drag(vm, "", 21, 40, 31, 45);
            Assert.Null(vm.Selection);
            Assert.Equal(new PixelRect(30, 25, 61, 41), single<RectangleAnnotation>(vm).Rect);
            Assert.NotNull(vm.SelectedAnnotation);
            // im Leeren aufziehen → Bereich wie bisher, Objektauswahl aufgehoben
            drag(vm, "", 200, 150, 260, 200);
            Assert.Equal(new PixelRect(200, 150, 61, 51), vm.Selection);
            Assert.Null(vm.SelectedAnnotation);
            // Innenfläche des ungefüllten Rechtecks ist kein Treffer
            Assert.Null(vm.HitTestAnnotation(at(60, 45)));
        });
    }

    [Fact]
    public void TextTool_EditCommitCancel_AndDoubleClickReopens() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            int editEvents = 0;
            vm.TextEditChanged += (s, e) => editEvents++;
            click(vm, TextTool.ToolKey, 100, 100);
            Assert.True(vm.IsTextEditing);
            Assert.True(vm.IsEditingNewText);
            Assert.Empty(vm.Document.Annotations);
            vm.UpdateEditingText("Hallo");
            vm.CommitTextEdit();
            TextAnnotation text = single<TextAnnotation>(vm);
            Assert.Equal("Hallo", text.Text);
            Assert.Equal(100, text.X + (int)TextLayout.GetPadding(text.FontSize));
            Assert.Equal(1, vm.UndoStack.UndoCount);
            Assert.True(editEvents >= 2);

            // leerer neuer Text → nichts
            click(vm, "", 300, 200);
            vm.CommitTextEdit();
            Assert.Single(vm.Document.Annotations);
            Assert.Equal(1, vm.UndoStack.UndoCount);

            // Doppelklick in „Auswahl“ öffnet die Bearbeitung
            vm.SelectTool(vm.ObjectSelectTool);
            click(vm, "", 105, 105);
            click(vm, "", 105, 105, 2);
            Assert.True(vm.IsTextEditing);
            Assert.False(vm.IsEditingNewText);
            Assert.False(vm.IsAnnotationDrawn(vm.Document.Annotations[0]));
            vm.UpdateEditingText("Welt");
            vm.CommitTextEdit();
            Assert.Equal("Welt", single<TextAnnotation>(vm).Text);
            Assert.Equal(Strings.UndoEditText, vm.UndoStack.UndoDescription);

            // Abbrechen lässt den Text unverändert
            click(vm, "", 105, 105, 2);
            vm.UpdateEditingText("verworfen");
            vm.CancelTextEdit();
            Assert.Equal("Welt", single<TextAnnotation>(vm).Text);

            // leerer Text löscht das Objekt
            click(vm, "", 105, 105, 2);
            vm.UpdateEditingText("  ");
            vm.CommitTextEdit();
            Assert.Empty(vm.Document.Annotations);
            vm.Undo();
            Assert.Equal("Welt", single<TextAnnotation>(vm).Text);
        });
    }

    [Fact]
    public void TextTool_DragSetsWrapWidth() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            vm.Options.FontSize = 20;
            drag(vm, TextTool.ToolKey, 50, 50, 250, 90);
            Assert.True(vm.IsTextEditing);
            Assert.Null(vm.ToolPreviewRect);
            TextAnnotation editing = vm.EditingText!;
            Assert.Equal(50, editing.X);
            Assert.Equal(50, editing.Y);
            Assert.Equal(201 - 2 * (int)TextLayout.GetPadding(20), editing.Width);
        });
    }

    [Fact]
    public void Options_ApplyToSelectedObject_AsUndoStep() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            drag(vm, RectangleTool.ToolKey, 20, 20, 80, 60);
            Assert.True(vm.ShowColorOption && vm.ShowThicknessOption && vm.ShowFillColorOption);
            Assert.False(vm.ShowBlockSizeOption || vm.ShowFontSizeOption || vm.ShowCursorKindOption || vm.ShowLineCapOptions || vm.ShowTextBackgroundOption);
            click(vm, ObjectSelectTool.ToolKey, 21, 40);
            Assert.NotNull(vm.SelectedAnnotation);
            int steps = vm.UndoStack.UndoCount;

            vm.SetColor("#FF00FF00");
            vm.SetThickness(12);
            vm.SetFillColor("#801E88E5");
            RectangleAnnotation rectangle = single<RectangleAnnotation>(vm);
            Assert.Equal(("#FF00FF00", 12.0, "#801E88E5"), (rectangle.Stroke, rectangle.Thickness, rectangle.Fill));
            Assert.Equal(steps + 3, vm.UndoStack.UndoCount);
            Assert.Equal("#FF00FF00", vm.CurrentColor);
            Assert.Equal("#801E88E5", vm.CurrentFillColor);
            Assert.Equal("#FF00FF00", vm.Options.Color);
            Assert.Equal("#801E88E5", vm.Options.FillColor);
            // Rahmenfarbe ändern lässt die eigene Füllfarbe stehen
            vm.SetColor("#FF000000");
            Assert.Equal(("#FF000000", "#801E88E5"), (single<RectangleAnnotation>(vm).Stroke, single<RectangleAnnotation>(vm).Fill));
            vm.Undo();
            // gleicher Wert → kein Schritt
            vm.SetThickness(12);
            Assert.Equal(steps + 3, vm.UndoStack.UndoCount);

            vm.Undo();
            vm.Undo();
            vm.Undo();
            rectangle = single<RectangleAnnotation>(vm);
            Assert.Equal((AnnotationColors.Red, 4.0, (string?)null), (rectangle.Stroke, rectangle.Thickness, rectangle.Fill));
            Assert.NotNull(vm.SelectedAnnotation);

            // Zeichenwerkzeug wählen hebt die Objektauswahl auf
            vm.SelectToolByKey(PixelateTool.ToolKey);
            Assert.Null(vm.SelectedAnnotation);
            Assert.True(vm.ShowBlockSizeOption);
            Assert.False(vm.ShowColorOption);
        });
    }

    [Fact]
    public void VirtualMouse_CanBeHiddenAndShown_WithUndo() {
        Sta.Run(() => {
            CaptureDocument document = new CaptureDocument {
                Annotations = new List<Annotation> { new CursorAnnotation { X = 100, Y = 100, Kind = CursorKinds.Arrow } }
            };
            EditorViewModel vm = create(document);
            click(vm, ObjectSelectTool.ToolKey, 104, 108);
            Assert.True(vm.ShowCursorVisibleOption);
            Assert.True(vm.ShowCursorKindOption);
            Assert.Empty(vm.GetSelectionVisual()!.Handles);
            vm.ToggleCursorVisible();
            Assert.False(single<CursorAnnotation>(vm).Visible);
            Assert.Single(vm.GetHiddenCursorBounds());
            // ausgeblendet bleibt auswählbar und verschiebbar
            drag(vm, "", 104, 108, 124, 118);
            Assert.Equal((120, 110), (single<CursorAnnotation>(vm).X, single<CursorAnnotation>(vm).Y));
            vm.ToggleCursorVisible();
            Assert.True(single<CursorAnnotation>(vm).Visible);
            vm.SetCursorKind(CursorKinds.Hand);
            Assert.Equal(CursorKinds.Hand, single<CursorAnnotation>(vm).Kind);
            Assert.Equal(4, vm.UndoStack.UndoCount);
            vm.Undo();
            vm.Undo();
            vm.Undo();
            vm.Undo();
            Assert.Equal((100, 100, true, CursorKinds.Arrow), (single<CursorAnnotation>(vm).X, single<CursorAnnotation>(vm).Y, single<CursorAnnotation>(vm).Visible, single<CursorAnnotation>(vm).Kind));
        });
    }

    [Fact]
    public void Duplicate_CopiesWithOffsetAndNewId() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            click(vm, NumberStampTool.ToolKey, 50, 50);
            click(vm, ObjectSelectTool.ToolKey, 50, 50);
            vm.DuplicateSelectedAnnotation();
            Assert.Equal(2, vm.Document.Annotations.Count);
            NumberStampAnnotation copy = (NumberStampAnnotation)vm.Document.Annotations[1];
            Assert.NotEqual(vm.Document.Annotations[0].Id, copy.Id);
            Assert.Equal((60, 60, 2), (copy.X, copy.Y, copy.Number));
            Assert.Equal(copy.Id, vm.SelectedAnnotationId);
            vm.Undo();
            Assert.Single(vm.Document.Annotations);
        });
    }

    [Fact]
    public void SixtySteps_MixedRegionAndDrawing_UndoRedoIdentical() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            List<string> states = new List<string> { CaptureDocumentSerializer.Serialize(vm.Document) };
            string[] tools = { RectangleTool.ToolKey, EllipseTool.ToolKey, ArrowTool.ToolKey, HighlightTool.ToolKey, PixelateTool.ToolKey, BlurTool.ToolKey };
            for(int i = 0; i < 60; i++) {
                if(i % 3 == 0) {
                    vm.SetSelection(new PixelRect(i, i, 100 + i, 50 + i));
                } else {
                    drag(vm, tools[i % tools.Length], 5 + i * 3, 5 + i * 2, 40 + i * 3, 30 + i * 2);
                }
                states.Add(CaptureDocumentSerializer.Serialize(vm.Document));
            }
            Assert.Equal(60, vm.UndoStack.UndoCount);
            for(int i = 60; i > 0; i--) {
                Assert.True(vm.Undo());
                Assert.Equal(states[i - 1], CaptureDocumentSerializer.Serialize(vm.Document));
            }
            for(int i = 1; i <= 60; i++) {
                Assert.True(vm.Redo());
                Assert.Equal(states[i], CaptureDocumentSerializer.Serialize(vm.Document));
            }
        });
    }

    [Fact]
    public void ToolOptions_RoundTripThroughSettings() {
        AppSettings settings = new AppSettings();
        ToolOptions options = ToolOptions.FromSettings(settings);
        Assert.False(options.ApplyTo(settings));
        options.FontSize = 48;
        options.BlockSize = 20;
        options.BlurRadius = 16;
        options.FillColor = "#80E53935";
        options.TextBackground = true;
        options.Smooth = false;
        options.StampSize = 48;
        options.CursorKind = CursorKinds.Hand;
        options.Color = "#FF000000";
        options.Thickness = 8;
        Assert.True(options.ApplyTo(settings));
        Assert.Equal((48.0, 20, 16, "#80E53935", true, false, 48.0, CursorKinds.Hand, "#FF000000", 8.0),
            (settings.DefaultFontSize, settings.DefaultBlockSize, settings.DefaultBlurRadius, settings.DefaultFillColor, settings.DefaultTextBackground,
             settings.DefaultSmooth, settings.DefaultStampSize, settings.DefaultCursorKind, settings.DefaultColor, settings.DefaultThickness));
        ToolOptions reloaded = ToolOptions.FromSettings(settings);
        Assert.Equal(48, reloaded.FontSize);
        Assert.False(reloaded.ApplyTo(settings));
    }

    [Fact]
    public void DrawnAnnotation_IsSelected_AndColorChangeAppliesToIt() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            vm.Options.Color = "#FF112233";
            drag(vm, RectangleTool.ToolKey, 10, 10, 50, 40);
            RectangleAnnotation rectangle = single<RectangleAnnotation>(vm);
            Assert.Same(rectangle, vm.SelectedAnnotation);

            // Farbe ändern wirkt auf das gerade gezeichnete Objekt (ein Rückgängig-Schritt) und wird als Standard gemerkt
            vm.SetColor("#FF00FF00");
            Assert.Equal("#FF00FF00", single<RectangleAnnotation>(vm).Stroke);
            Assert.Equal("#FF00FF00", vm.Options.Color);
            Assert.Equal(2, vm.UndoStack.UndoCount);

            // Die nächste Zeichnung hebt die Auswahl auf und wird dann selbst ausgewählt
            drag(vm, EllipseTool.ToolKey, 100, 100, 150, 140);
            Assert.IsType<EllipseAnnotation>(vm.SelectedAnnotation);
            Assert.Equal("#FF00FF00", single<EllipseAnnotation>(vm).Stroke);

            // Stempel und Zeiger werden nach dem Setzen ebenfalls ausgewählt
            click(vm, NumberStampTool.ToolKey, 200, 200);
            Assert.IsType<NumberStampAnnotation>(vm.SelectedAnnotation);
            click(vm, CursorTool.ToolKey, 250, 200);
            Assert.IsType<CursorAnnotation>(vm.SelectedAnnotation);

            // Rückgängig entfernt das ausgewählte Objekt – keine hängende Auswahl
            vm.Undo();
            Assert.Null(vm.SelectedAnnotation);
        });
    }

    [Fact]
    public void LineCaps_ApplyToSelectedObject_AsOneUndoStep_AndAreRememberedPerType() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.True(vm.SelectToolByKey(ArrowTool.ToolKey));
            Assert.True(vm.ShowLineCapOptions);
            Assert.False(vm.ShowFillColorOption);
            Assert.Equal((LineCaps.None, LineCaps.Arrow), (vm.CurrentStartCap, vm.CurrentEndCap));
            Assert.True(vm.SelectToolByKey(LineTool.ToolKey));
            Assert.Equal((LineCaps.None, LineCaps.None), (vm.CurrentStartCap, vm.CurrentEndCap));

            drag(vm, LineTool.ToolKey, 20, 20, 120, 60);
            LineAnnotation line = single<LineAnnotation>(vm);
            Assert.Same(line, vm.SelectedAnnotation);
            int steps = vm.UndoStack.UndoCount;

            vm.SetEndCap(LineCaps.Arrow);
            Assert.Equal(LineCaps.Arrow, single<LineAnnotation>(vm).EndCap);
            Assert.Equal(steps + 1, vm.UndoStack.UndoCount);
            vm.SetStartCap(LineCaps.Dot);
            Assert.Equal((LineCaps.Dot, LineCaps.Arrow), (single<LineAnnotation>(vm).StartCap, single<LineAnnotation>(vm).EndCap));
            Assert.Equal(steps + 2, vm.UndoStack.UndoCount);
            Assert.Equal((LineCaps.Dot, LineCaps.Arrow), (vm.CurrentStartCap, vm.CurrentEndCap));
            // gleicher Wert → kein Schritt
            vm.SetStartCap(LineCaps.Dot);
            Assert.Equal(steps + 2, vm.UndoStack.UndoCount);

            // als Standard gemerkt – nur für Linien, Pfeil und Freihand behalten ihre eigenen Werte
            Assert.Equal((LineCaps.Dot, LineCaps.Arrow), (vm.Options.LineStartCap, vm.Options.LineEndCap));
            Assert.Equal((LineCaps.None, LineCaps.Arrow), (vm.Options.ArrowStartCap, vm.Options.ArrowEndCap));
            Assert.Equal((LineCaps.None, LineCaps.None), (vm.Options.FreehandStartCap, vm.Options.FreehandEndCap));
            AppSettings settings = new AppSettings();
            Assert.True(vm.StoreOptions(settings));
            Assert.Equal((LineCaps.Dot, LineCaps.Arrow, LineCaps.None, LineCaps.Arrow), (settings.DefaultLineStartCap, settings.DefaultLineEndCap, settings.DefaultArrowStartCap, settings.DefaultArrowEndCap));

            vm.Undo();
            Assert.Equal((LineCaps.None, LineCaps.Arrow), (single<LineAnnotation>(vm).StartCap, single<LineAnnotation>(vm).EndCap));
            vm.Undo();
            Assert.Equal((LineCaps.None, LineCaps.None), (single<LineAnnotation>(vm).StartCap, single<LineAnnotation>(vm).EndCap));

            // neue Freihand-Linie bekommt ihre eigenen Standards; Änderung wirkt auf sie
            vm.Options.FreehandEndCap = LineCaps.Triangle;
            drag(vm, FreehandTool.ToolKey, 50, 150, 200, 220);
            FreehandAnnotation freehand = single<FreehandAnnotation>(vm);
            Assert.Equal((LineCaps.None, LineCaps.Triangle), (freehand.StartCap, freehand.EndCap));
            vm.SetStartCap(LineCaps.Square);
            Assert.Equal(LineCaps.Square, single<FreehandAnnotation>(vm).StartCap);
            Assert.Equal(LineCaps.Square, vm.Options.FreehandStartCap);
            Assert.Equal(LineCaps.Dot, vm.Options.LineStartCap);
        });
    }

    [Fact]
    public void FillColor_NewObjectsUseDefault_TextKeepsBackgroundToggle() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            Assert.True(vm.SelectToolByKey(EllipseTool.ToolKey));
            Assert.True(vm.ShowFillColorOption);
            Assert.False(vm.ShowTextBackgroundOption || vm.ShowLineCapOptions);
            Assert.Equal("", vm.CurrentFillColor);
            Assert.Equal(1 + 2 * AnnotationColors.Palette.Count, vm.FillColorChoices.Count);
            Assert.Equal("", vm.FillColorChoices[0].Value);
            Assert.Contains(vm.FillColorChoices, c => (string)c.Value == "#80E53935");

            // ohne Auswahl: nur Standard ändern, kein Rückgängig-Schritt
            vm.SetFillColor("#8043A047");
            Assert.Equal(0, vm.UndoStack.UndoCount);
            drag(vm, EllipseTool.ToolKey, 20, 20, 80, 60);
            Assert.Equal("#8043A047", single<EllipseAnnotation>(vm).Fill);
            drag(vm, RectangleTool.ToolKey, 120, 20, 180, 60);
            Assert.Equal("#8043A047", single<RectangleAnnotation>(vm).Fill);
            // gefüllt: auch innen ein Treffer
            click(vm, ObjectSelectTool.ToolKey, 150, 40);
            Assert.IsType<RectangleAnnotation>(vm.SelectedAnnotation);
            // „keine“ entfernt die Füllung (ein Schritt)
            int steps = vm.UndoStack.UndoCount;
            vm.SetFillColor("");
            Assert.Null(single<RectangleAnnotation>(vm).Fill);
            Assert.Equal(steps + 1, vm.UndoStack.UndoCount);
            AppSettings settings = new AppSettings();
            vm.StoreOptions(settings);
            Assert.Equal("", settings.DefaultFillColor);

            Assert.True(vm.SelectToolByKey(TextTool.ToolKey));
            Assert.True(vm.ShowTextBackgroundOption);
            Assert.False(vm.ShowFillColorOption);
            Assert.False(vm.IsTextBackgroundActive);
            vm.ToggleTextBackground();
            Assert.True(vm.Options.TextBackground);
        });
    }
}
