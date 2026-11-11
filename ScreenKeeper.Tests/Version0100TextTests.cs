using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Tools;

namespace ScreenKeeper.Tests;

/// <summary>
/// 0.10.0: Text-Werkzeug mit festem Textfeld (aufziehen, skalieren), automatischer und eigener Schriftgröße,
/// Abschneiden am Feldrand, 9 Ausrichtungen und gemerkten Text-Einstellungen.
/// </summary>
public class TextFieldTests {
    private const string longText = "Ein ziemlich langer Text mit vielen Wörtern, der in einem kleinen Feld umbrochen werden muss.";

    private static EditorViewModel create(AppSettings? settings = null) {
        CaptureDocument document = new CaptureDocument {
            VirtualScreen = new PixelRect(0, 0, 400, 300),
            Monitors = new List<MonitorInfo> { new MonitorInfo { Bounds = new PixelRect(0, 0, 400, 300), Primary = true } }
        };
        return new EditorViewModel(document, TestImages.CreatePattern(400, 300), new TestEditorHost(), settings ?? new AppSettings());
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

    private static TextAnnotation field(string text, int width, int height, bool auto = false, string alignment = TextAlignments.TopLeft, double fontSize = 24) {
        return new TextAnnotation { X = 10, Y = 20, Text = text, Width = width, Height = height, AutoFontSize = auto, Alignment = alignment, FontSize = fontSize };
    }

    private static Rect drawingBounds(Annotation annotation) {
        CaptureDocument document = new CaptureDocument { Annotations = new List<Annotation> { annotation } };
        BitmapSource original = TestImages.CreatePattern(600, 400);
        return new RenderService().BuildAnnotationDrawing(document, original).Bounds;
    }

    [Fact]
    public void TextTool_DragLikeRectangle_ShiftSquare_SmallDragGivesFreeText() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            drag(vm, TextTool.ToolKey, 20, 30, 120, 60, ModifierKeys.Shift);
            TextAnnotation square = vm.EditingText!;
            Assert.Equal((20, 30, 101, 101), (square.X, square.Y, square.Width!.Value, square.Height!.Value));
            vm.CancelTextEdit();

            // Vorschau-Rahmen während des Ziehens, danach weg
            vm.PointerDown(at(50, 50, ModifierKeys.None, 1));
            vm.PointerMove(at(90, 80));
            Assert.Equal(new PixelRect(50, 50, 41, 31), vm.ToolPreviewRect);
            vm.PointerUp(at(90, 80));
            Assert.Null(vm.ToolPreviewRect);
            vm.CancelTextEdit();

            // Ziehen über den Bildrand: wie beim Rechteck auf das Bild begrenzt
            drag(vm, "", 350, 250, 500, 400);
            TextAnnotation clamped = vm.EditingText!;
            Assert.Equal((350, 250, 50, 50), (clamped.X, clamped.Y, clamped.Width!.Value, clamped.Height!.Value));
            vm.CancelTextEdit();

            // zu flach: freier Text an der linken oberen Ecke (wie ein Klick)
            drag(vm, "", 100, 100, 200, 104);
            TextAnnotation free = vm.EditingText!;
            Assert.False(TextLayout.IsField(free));
            Assert.Null(free.Height);
            Assert.Null(free.Width);
            vm.CancelTextEdit();
        });
    }

    [Fact]
    public void TextTool_Click_StillCreatesFreeText_WithFixedSize() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            vm.SetAutoFontSize();
            vm.SetFontSize(20);
            vm.SetAutoFontSize();
            Assert.True(vm.Options.TextAutoFontSize);
            Assert.True(vm.SelectToolByKey(TextTool.ToolKey));
            vm.PointerDown(at(100, 100, ModifierKeys.None, 1));
            vm.PointerUp(at(100, 100));
            TextAnnotation free = vm.EditingText!;
            Assert.False(TextLayout.IsField(free));
            Assert.False(free.AutoFontSize);
            Assert.Equal(20, free.FontSize);
            Assert.Equal(20, TextLayout.GetEffectiveFontSize(free));
            // Auto am freien Text: der Text behält seine feste Größe
            vm.SetAutoFontSize();
            Assert.False(vm.EditingText!.AutoFontSize);
            Assert.False(vm.IsAutoFontSizeActive);
            vm.CancelTextEdit();
        });
    }

    [Fact]
    public void AutoFontSize_ShrinksWithMoreText_AndGrowsWithLargerField() {
        Sta.Run(() => {
            double shortSize = TextLayout.GetEffectiveFontSize(field("Hallo", 200, 80, true));
            double longSize = TextLayout.GetEffectiveFontSize(field("Hallo Welt, das ist mehr Text", 200, 80, true));
            double longerSize = TextLayout.GetEffectiveFontSize(field(longText, 200, 80, true));
            Assert.True(shortSize > longSize, $"{shortSize} > {longSize}");
            Assert.True(longSize > longerSize, $"{longSize} > {longerSize}");
            double largerField = TextLayout.GetEffectiveFontSize(field(longText, 400, 200, true));
            Assert.True(largerField > longerSize, $"{largerField} > {longerSize}");
            // Raster 0,5 und Grenzen
            foreach(double size in new[] { shortSize, longSize, longerSize, largerField }) {
                Assert.Equal(0, size % TextLayout.AutoFontSizeStep);
                Assert.InRange(size, TextLayout.MinFontSize, TextLayout.MaxFontSize);
            }
            // eine Zeile füllt höchstens die Feldhöhe
            Assert.True(shortSize < 80);
            // deterministisch (und aus dem Zwischenspeicher gleich)
            Assert.Equal(longerSize, TextLayout.GetEffectiveFontSize(field(longText, 200, 80, true)));
            // feste Größe bleibt fest
            Assert.Equal(24, TextLayout.GetEffectiveFontSize(field(longText, 200, 80, false)));
        });
    }

    [Fact]
    public void AutoFontSize_TextFitsInField_NextStepWouldNot() {
        Sta.Run(() => {
            TextAnnotation text = field(longText, 220, 90, true);
            TextArrangement layout = TextLayout.Arrange(text, System.Windows.Media.Brushes.Black);
            Assert.True(layout.IsField);
            Assert.Equal(new Rect(10, 20, 220, 90), layout.Bounds);
            Assert.Equal(TextLayout.GetPadding(layout.FontSize), layout.Padding);
            Assert.True(layout.Text.Height <= 90 - 2 * layout.Padding + 0.01);
            Assert.True(layout.Text.MinWidth <= 220 - 2 * layout.Padding + 0.01);
            // ein Schritt größer passt nicht mehr
            TextAnnotation bigger = field(longText, 220, 90, false, TextAlignments.TopLeft, layout.FontSize + TextLayout.AutoFontSizeStep);
            TextArrangement next = TextLayout.Arrange(bigger, System.Windows.Media.Brushes.Black);
            Assert.True(next.Text.Height > 90 - 2 * next.Padding || next.Text.MinWidth > 220 - 2 * next.Padding);
            // passt auch bei der Mindestgröße nicht → Mindestgröße (Rest wird abgeschnitten)
            string huge = string.Join(" ", Enumerable.Repeat(longText, 20));
            Assert.Equal(TextLayout.MinFontSize, TextLayout.GetEffectiveFontSize(field(huge, 60, 30, true)));
            // leerer Text: eine Zeile, die das Feld füllt
            Assert.True(TextLayout.GetEffectiveFontSize(field("", 300, 40, true)) > 20);
        });
    }

    [Fact]
    public void FixedField_ClipsOverflowingText_InDrawing_ButFreeTextIsNotClipped() {
        Sta.Run(() => {
            TextAnnotation overflowing = field(longText, 120, 40, false, TextAlignments.TopLeft, 24);
            Rect bounds = drawingBounds(overflowing);
            Assert.False(bounds.IsEmpty);
            Rect fieldRect = new Rect(10, 20, 120, 40);
            Assert.True(fieldRect.Contains(bounds), $"{bounds} liegt nicht in {fieldRect}");
            // mit Hintergrund: genau das Feld
            overflowing.Background = "#F0FFFFFF";
            Assert.Equal(fieldRect, drawingBounds(overflowing));
            // freier Text mit Umbruchbreite (alte Dateien): wächst nach unten, kein Abschneiden
            TextAnnotation free = new TextAnnotation { X = 10, Y = 20, Text = longText, Width = 120, FontSize = 24 };
            Assert.True(drawingBounds(free).Height > 100);
        });
    }

    [Fact]
    public void Alignment_PositionsTextInField() {
        Sta.Run(() => {
            Dictionary<string, TextArrangement> layouts = TextAlignments.All.ToDictionary(a => a, a => TextLayout.Arrange(field("Hallo", 300, 120, false, a, 20), System.Windows.Media.Brushes.Black));
            TextArrangement topLeft = layouts[TextAlignments.TopLeft];
            double padding = topLeft.Padding;
            double textHeight = topLeft.Text.Height;
            Assert.Equal(20 + padding, layouts[TextAlignments.TopLeft].Origin.Y, 3);
            Assert.Equal(20 + padding + (120 - 2 * padding - textHeight) / 2, layouts[TextAlignments.MiddleCenter].Origin.Y, 3);
            Assert.Equal(20 + 120 - padding - textHeight, layouts[TextAlignments.BottomRight].Origin.Y, 3);
            Assert.Equal(System.Windows.TextAlignment.Left, layouts[TextAlignments.BottomLeft].Text.TextAlignment);
            Assert.Equal(System.Windows.TextAlignment.Center, layouts[TextAlignments.TopCenter].Text.TextAlignment);
            Assert.Equal(System.Windows.TextAlignment.Right, layouts[TextAlignments.MiddleRight].Text.TextAlignment);
            // gezeichnet: links < Mitte < rechts, oben < Mitte < unten
            Rect left = drawingBounds(field("Hallo", 300, 120, false, TextAlignments.TopLeft, 20));
            Rect center = drawingBounds(field("Hallo", 300, 120, false, TextAlignments.MiddleCenter, 20));
            Rect right = drawingBounds(field("Hallo", 300, 120, false, TextAlignments.BottomRight, 20));
            Assert.True(left.X < center.X && center.X < right.X, $"{left.X} < {center.X} < {right.X}");
            Assert.True(left.Y < center.Y && center.Y < right.Y, $"{left.Y} < {center.Y} < {right.Y}");
            Assert.True(right.Right <= 10 + 300 - padding + 1);
            // unbekannter Wert: wie oben links
            Assert.Equal(topLeft.Origin, TextLayout.Arrange(field("Hallo", 300, 120, false, "zukunft", 20), System.Windows.Media.Brushes.Black).Origin);
        });
    }

    [Fact]
    public void Alignment_FreeText_KeepsBounds_AndAlignsLines() {
        Sta.Run(() => {
            TextAnnotation left = new TextAnnotation { X = 5, Y = 5, Text = "kurz\nviel längere Zeile", FontSize = 20 };
            TextAnnotation right = new TextAnnotation { X = 5, Y = 5, Text = "kurz\nviel längere Zeile", FontSize = 20, Alignment = TextAlignments.BottomRight };
            // Feld bleibt gleich (vertikale Ausrichtung wirkt bei freiem Text nicht)
            Assert.Equal(TextLayout.GetBounds(left), TextLayout.GetBounds(right));
            Assert.Equal(TextLayout.Arrange(left, System.Windows.Media.Brushes.Black).Origin, TextLayout.Arrange(right, System.Windows.Media.Brushes.Black).Origin);
            // die lange Zeile bestimmt die Breite: gezeichnet liegt der Text (fast) an derselben Stelle, nur rechtsbündig
            Rect leftDrawing = drawingBounds(left);
            Rect rightDrawing = drawingBounds(right);
            Assert.Equal(leftDrawing.Height, rightDrawing.Height, 1);
            Assert.InRange(rightDrawing.X - leftDrawing.X, -0.01, 1.5);
            Assert.Equal(System.Windows.TextAlignment.Right, TextLayout.Arrange(right, System.Windows.Media.Brushes.Black).Text.TextAlignment);
        });
    }

    [Fact]
    public void Resize_Field_LikeRectangle_FontSizeUnchanged() {
        FakeMeasure measure = new FakeMeasure();
        TextAnnotation text = field("abc", 100, 50, false, TextAlignments.TopLeft, 20);
        // rechts unten (Pixel 109/69 → Anfasser 110/70) um 30/20 ziehen
        TextAnnotation larger = (TextAnnotation)AnnotationEditing.Resized(text, ObjectHandle.BottomRight, new PixelPoint(110, 70), new PixelPoint(140, 90), false, measure);
        Assert.Equal((10, 20, 130, 70), (larger.X, larger.Y, larger.Width!.Value, larger.Height!.Value));
        Assert.Equal(20, larger.FontSize);
        // oben: nur die Höhe
        TextAnnotation taller = (TextAnnotation)AnnotationEditing.Resized(text, ObjectHandle.Top, new PixelPoint(60, 20), new PixelPoint(60, 0), false, measure);
        Assert.Equal((10, 0, 100, 70), (taller.X, taller.Y, taller.Width!.Value, taller.Height!.Value));
        // links über die rechte Kante hinaus → klappt um wie beim Rechteck
        TextAnnotation flipped = (TextAnnotation)AnnotationEditing.Resized(text, ObjectHandle.Left, new PixelPoint(10, 40), new PixelPoint(130, 40), false, measure);
        Assert.Equal((109, 20, 22, 50), (flipped.X, flipped.Y, flipped.Width!.Value, flipped.Height!.Value));
        // Original unverändert, 8 Anfasser
        Assert.Equal((10, 20, 100, 50), (text.X, text.Y, text.Width!.Value, text.Height!.Value));
        Assert.Equal(8, AnnotationEditing.GetHandles(text).Count);
    }

    [Fact]
    public void Resize_Field_InEditor_AutoFontSizeFollows() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            vm.SetAutoFontSize();
            drag(vm, TextTool.ToolKey, 20, 20, 119, 59);
            vm.UpdateEditingText(longText);
            vm.CommitTextEdit();
            TextAnnotation text = Assert.Single(vm.Document.Annotations.OfType<TextAnnotation>());
            Assert.True(text.AutoFontSize);
            Assert.Equal(text.Id, vm.SelectedAnnotationId);
            double before = TextLayout.GetEffectiveFontSize(text);
            Assert.Equal(new Rect(20, 20, 100, 40), vm.Measure.GetTextBounds(text));

            // Anfasser rechts unten (120/60) mit „Auswahl“ ziehen: Feld wächst, Schrift folgt
            vm.SelectTool(vm.ObjectSelectTool);
            drag(vm, "", 120, 60, 300, 160);
            TextAnnotation resized = Assert.Single(vm.Document.Annotations.OfType<TextAnnotation>());
            Assert.Equal((20, 20, 280, 140), (resized.X, resized.Y, resized.Width!.Value, resized.Height!.Value));
            Assert.True(TextLayout.GetEffectiveFontSize(resized) > before);
            Assert.Equal(Strings.Format(Strings.FontSizeAutoValue, Strings.Format(Strings.OptionValuePixels, TextLayout.GetEffectiveFontSize(resized).ToString("0.#"))), vm.CurrentFontSizeText);
            Assert.Equal(24, resized.FontSize);
            vm.Undo();
            Assert.Equal(100, Assert.Single(vm.Document.Annotations.OfType<TextAnnotation>()).Width);
        });
    }

    [Fact]
    public void TryParseFontSize_AcceptsDecimalsAndUnit_ClampsAndRejects() {
        Assert.True(EditorViewModel.TryParseFontSize("18", out double plain));
        Assert.Equal(18, plain);
        Assert.True(EditorViewModel.TryParseFontSize(" 18,5 ", out double comma));
        Assert.Equal(18.5, comma);
        Assert.True(EditorViewModel.TryParseFontSize("18.3", out double rounded));
        Assert.Equal(18.5, rounded);
        Assert.True(EditorViewModel.TryParseFontSize("30 px", out double unit));
        Assert.Equal(30, unit);
        Assert.True(EditorViewModel.TryParseFontSize("1000", out double large));
        Assert.Equal(TextLayout.MaxFontSize, large);
        Assert.True(EditorViewModel.TryParseFontSize("2", out double small));
        Assert.Equal(TextLayout.MinFontSize, small);
        foreach(string invalid in new[] { "", "  ", "abc", "-5", "0", "NaN", "1e999" }) {
            Assert.False(EditorViewModel.TryParseFontSize(invalid, out _), invalid);
        }
    }

    [Fact]
    public void CustomFontSize_AppliesToEditedText_AndTurnsAutoOff() {
        Sta.Run(() => {
            EditorViewModel vm = create();
            vm.SetAutoFontSize();
            drag(vm, TextTool.ToolKey, 20, 20, 220, 120);
            Assert.True(vm.EditingText!.AutoFontSize);
            Assert.True(vm.IsAutoFontSizeActive);
            Assert.False(vm.ApplyFontSizeInput("x"));
            Assert.True(vm.EditingText!.AutoFontSize);
            Assert.True(vm.ApplyFontSizeInput("37"));
            Assert.False(vm.EditingText!.AutoFontSize);
            Assert.Equal(37, vm.EditingText!.FontSize);
            Assert.Equal(Strings.Format(Strings.OptionValuePixels, "37"), vm.CurrentFontSizeText);
            Assert.False(vm.Options.TextAutoFontSize);
            Assert.Equal(37, vm.Options.FontSize);
            vm.CancelTextEdit();
            Assert.Equal(Strings.Format(Strings.OptionValuePixels, "37"), vm.CurrentFontSizeText);
            vm.SetAutoFontSize();
            Assert.Equal(Strings.FontSizeAuto, vm.CurrentFontSizeText);
            Assert.Equal("37", vm.CurrentFontSizeValueText);
            Assert.Equal(OptionKind.AutoFontSize, vm.FontSizeChoices[0].Kind);
            Assert.Equal(9, vm.TextAlignmentChoices.Count);
        });
    }

    [Fact]
    public void TextSettings_AreRemembered_FromEditedOrSelectedText_AndUsedForNextField() {
        using (TempFolder temp = new TempFolder()) {
            Sta.Run(() => runRememberedSettings(Path.Combine(temp.Path, "settings.json")));
        }
    }

    private static void runRememberedSettings(string path) {
        SettingsService service = new SettingsService(path);
        service.Load();
        AppSettings settings = service.Settings;
        EditorViewModel vm = create(settings);
        drag(vm, TextTool.ToolKey, 20, 20, 220, 120);
        // während der Bearbeitung geändert
        vm.SetTextAlignment(TextAlignments.MiddleCenter);
        vm.SetAutoFontSize();
        vm.ToggleTextBackground();
        vm.SetColor("#FF1E88E5");
        vm.UpdateEditingText("Hallo");
        vm.CommitTextEdit();
        TextAnnotation first = Assert.Single(vm.Document.Annotations.OfType<TextAnnotation>());
        Assert.Equal(TextAlignments.MiddleCenter, first.Alignment);
        Assert.True(first.AutoFontSize);
        Assert.NotNull(first.Background);

        // am ausgewählten Text geändert (Rückgängig-Schritt) – ebenfalls gemerkt
        Assert.Equal(first.Id, vm.SelectedAnnotationId);
        int steps = vm.UndoStack.UndoCount;
        vm.SetTextAlignment(TextAlignments.BottomRight);
        Assert.Equal(steps + 1, vm.UndoStack.UndoCount);
        Assert.Equal(TextAlignments.BottomRight, Assert.Single(vm.Document.Annotations.OfType<TextAnnotation>()).Alignment);
        Assert.Equal(TextAlignments.BottomRight, vm.CurrentTextAlignment);

        Assert.True(vm.StoreOptions(settings));
        Assert.Equal(TextAlignments.BottomRight, settings.DefaultTextAlignment);
        Assert.True(settings.DefaultTextAutoFontSize);
        Assert.True(settings.DefaultTextBackground);
        Assert.Equal("#FF1E88E5", settings.DefaultColor);
        Assert.False(vm.StoreOptions(settings));

        // als JSON gespeichert und neu geladen → nächster Editor, nächstes Textfeld
        settings.DefaultFontSize = 30;
        service.Save();
        Assert.Contains("\"defaultTextAlignment\": \"bottomRight\"", File.ReadAllText(path));
        SettingsService reloaded = new SettingsService(path);
        reloaded.Load();
        EditorViewModel next = create(reloaded.Settings);
        Assert.Equal(TextAlignments.BottomRight, next.Options.TextAlignment);
        Assert.True(next.Options.TextAutoFontSize);
        Assert.Equal(30, next.Options.FontSize);
        Assert.True(next.SelectToolByKey(TextTool.ToolKey));
        Assert.Equal(Strings.FontSizeAuto, next.CurrentFontSizeText);
        Assert.Equal(TextAlignments.BottomRight, next.CurrentTextAlignment);
        drag(next, "", 50, 50, 250, 150);
        TextAnnotation created = next.EditingText!;
        Assert.Equal(TextAlignments.BottomRight, created.Alignment);
        Assert.True(created.AutoFontSize);
        Assert.Equal(30, created.FontSize);
        Assert.Equal("#FF1E88E5", created.Foreground);
        Assert.NotNull(created.Background);
        next.CancelTextEdit();
    }

    [Fact]
    public void Settings_UnknownTextAlignment_IsNormalized() {
        using (TempFolder temp = new TempFolder()) {
            string path = Path.Combine(temp.Path, "settings.json");
            File.WriteAllText(path, "{ \"defaultTextAlignment\": \"schief\", \"defaultTextAutoFontSize\": true }");
            SettingsService service = new SettingsService(path);
            service.Load();
            Assert.Equal(TextAlignments.TopLeft, service.Settings.DefaultTextAlignment);
            Assert.True(service.Settings.DefaultTextAutoFontSize);
            Assert.Equal(TextAlignments.TopLeft, ToolOptions.FromSettings(new AppSettings { DefaultTextAlignment = "x" }).TextAlignment);
        }
        // ohne gespeicherten Wert (neu oder nach dem Update): automatische Schriftgröße
        Assert.True(new AppSettings().DefaultTextAutoFontSize);
    }

    [Fact]
    public void Serialization_OldText_LoadsUnchanged_NewFieldsRoundTrip() {
        Sta.Run(() => {
            // Text aus 0.9 (ohne height/autoFontSize/alignment): freier Text mit Umbruchbreite ohne Innenabstand
            string old = "{ \"formatVersion\": 1, \"annotations\": [ { \"type\": \"text\", \"id\": \"a1\", \"x\": 10, \"y\": 20, \"text\": \"Hallo Welt\", \"fontSize\": 20, \"width\": 80 }, "
                + "{ \"type\": \"text\", \"id\": \"a2\", \"x\": 5, \"y\": 6, \"text\": \"frei\", \"fontSize\": 24 } ] }";
            CaptureDocument loaded = CaptureDocumentSerializer.Deserialize(old);
            TextAnnotation wrapped = Assert.IsType<TextAnnotation>(loaded.Annotations[0]);
            Assert.Null(wrapped.Height);
            Assert.False(wrapped.AutoFontSize);
            Assert.Equal(TextAlignments.TopLeft, wrapped.Alignment);
            Assert.False(TextLayout.IsField(wrapped));
            Rect bounds = TextLayout.GetBounds(wrapped);
            Assert.Equal(80 + 2 * TextLayout.GetPadding(20), bounds.Width);
            Assert.Equal(new Point(10 + TextLayout.GetPadding(20), 20 + TextLayout.GetPadding(20)), TextLayout.Arrange(wrapped, System.Windows.Media.Brushes.Black).Origin);
            TextAnnotation free = Assert.IsType<TextAnnotation>(loaded.Annotations[1]);
            Assert.Null(free.Width);
            Assert.Equal(24, TextLayout.GetEffectiveFontSize(free));

            // neues Feld: alle Werte bleiben erhalten
            TextAnnotation newField = new TextAnnotation { X = 1, Y = 2, Text = "x", Width = 150, Height = 60, AutoFontSize = true, Alignment = TextAlignments.BottomCenter };
            string json = CaptureDocumentSerializer.Serialize(new CaptureDocument { Annotations = new List<Annotation> { newField } });
            using (JsonDocument parsed = JsonDocument.Parse(json)) {
                JsonElement element = parsed.RootElement.GetProperty("annotations")[0];
                Assert.Equal(60, element.GetProperty("height").GetInt32());
                Assert.True(element.GetProperty("autoFontSize").GetBoolean());
                Assert.Equal("bottomCenter", element.GetProperty("alignment").GetString());
            }
            TextAnnotation roundTrip = (TextAnnotation)CaptureDocumentSerializer.Deserialize(json).Annotations[0];
            Assert.Equal((150, 60, true, TextAlignments.BottomCenter), (roundTrip.Width!.Value, roundTrip.Height!.Value, roundTrip.AutoFontSize, roundTrip.Alignment));
            Assert.Equal(json, CaptureDocumentSerializer.Serialize(CaptureDocumentSerializer.Deserialize(json)));

            // freier Text schreibt kein height
            string freeJson = CaptureDocumentSerializer.Serialize(new CaptureDocument { Annotations = new List<Annotation> { new TextAnnotation { Text = "x" } } });
            using (JsonDocument parsed = JsonDocument.Parse(freeJson)) {
                Assert.False(parsed.RootElement.GetProperty("annotations")[0].TryGetProperty("height", out _));
            }
            // unbekannte Ausrichtung bleibt erhalten
            string future = old.Replace("\"width\": 80", "\"width\": 80, \"alignment\": \"diagonal\"");
            Assert.Contains("\"diagonal\"", CaptureDocumentSerializer.Serialize(CaptureDocumentSerializer.Deserialize(future)));
        });
    }

    [Fact]
    public void TextAlignments_RowsAndColumns() {
        Assert.Equal(9, TextAlignments.All.Count);
        Assert.Equal((0, 0), (TextAlignments.GetRow(TextAlignments.TopLeft), TextAlignments.GetColumn(TextAlignments.TopLeft)));
        Assert.Equal((1, 1), (TextAlignments.GetRow(TextAlignments.MiddleCenter), TextAlignments.GetColumn(TextAlignments.MiddleCenter)));
        Assert.Equal((2, 2), (TextAlignments.GetRow(TextAlignments.BottomRight), TextAlignments.GetColumn(TextAlignments.BottomRight)));
        Assert.Equal((2, 0), (TextAlignments.GetRow(TextAlignments.BottomLeft), TextAlignments.GetColumn(TextAlignments.BottomLeft)));
        Assert.Equal((0, 0), (TextAlignments.GetRow(null), TextAlignments.GetColumn("x")));
        Assert.Equal(TextAlignments.TopRight, TextAlignments.Normalize(TextAlignments.TopRight, TextAlignments.TopLeft));
        Assert.Equal(TextAlignments.TopLeft, TextAlignments.Normalize("x", TextAlignments.TopLeft));
        Sta.Run(() => {
            foreach(string alignment in TextAlignments.All) {
                Assert.False(TextAlignmentIcons.Get(alignment).IsEmpty());
                Assert.False(string.IsNullOrEmpty(TextAlignmentIcons.GetLabel(alignment)));
            }
        });
    }
}
