using System.Windows.Input;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;
using ScreenKeeper.ViewModels.Tools;

namespace ScreenKeeper.Tests;

/// <summary>
/// 0.15.0: Zuschnitt neben dem Original (<c>.crop.png</c>), Texterkennung im Dokument (<c>ocr</c>/<c>ocrText</c>),
/// Suche über den erkannten Text und das Werkzeug „Text auswählen“.
/// </summary>
public class Version0150OcrTests {
    /// <summary>Zwei Zeilen: „Hallo schöne Welt“ (y 10–20) und „zweite Zeile“ (y 30–40), Bereich 0,0 200×100.</summary>
    private static RecognizedText sample(PixelRect? region = null) {
        return new RecognizedText {
            Region = region ?? new PixelRect(0, 0, 200, 100),
            Language = "de-DE",
            Lines = new List<RecognizedLine> {
                line("Hallo schöne Welt", word("Hallo", 10, 10, 30), word("schöne", 50, 10, 40), word("Welt", 100, 10, 30)),
                line("zweite Zeile", word("zweite", 10, 30, 40), word("Zeile", 60, 30, 35))
            }
        };
    }

    private static RecognizedWord word(string text, int x, int y, int width) {
        return new RecognizedWord { Text = text, Bounds = new PixelRect(x, y, width, 10) };
    }

    private static RecognizedLine line(string text, params RecognizedWord[] words) {
        return new RecognizedLine { Text = text, Words = words.ToList(), Bounds = OcrLayout.Union(words.Select(w => w.Bounds)) };
    }

    private static CaptureDocument document() {
        return new CaptureDocument {
            CapturedAt = new DateTimeOffset(2026, 11, 17, 9, 0, 0, TimeSpan.FromHours(1)),
            Image = "a.png",
            VirtualScreen = new PixelRect(0, 0, 200, 100),
            Monitors = new List<MonitorInfo> { new MonitorInfo { Bounds = new PixelRect(0, 0, 200, 100), Primary = true } }
        };
    }

    private static EditorViewModel createEditor(CaptureDocument doc) {
        return new EditorViewModel(doc, TestImages.CreatePattern(200, 100), new TestEditorHost(), new AppSettings());
    }

    private static PointerInput at(double x, double y, int clicks = 0) {
        return PointerInput.FromImage(x, y, ModifierKeys.None) with { ClickCount = clicks };
    }

    // ---------- JSON ----------

    [Fact]
    public void Serializer_RoundTrip_KeepsOnlyOcrText() {
        CaptureDocument doc = document();
        doc.Selection = new PixelRect(5, 5, 150, 50);
        doc.OcrText = sample(new PixelRect(5, 5, 150, 50)).GetText();

        string json = CaptureDocumentSerializer.Serialize(doc);
        Assert.Contains("\"ocrText\": \"Hallo schöne Welt\\nzweite Zeile\"", json);
        // keine Lage der Wörter in der JSON
        Assert.DoesNotContain("\"ocr\"", json);
        Assert.DoesNotContain("\"words\"", json);

        CaptureDocument loaded = CaptureDocumentSerializer.Deserialize(json);
        Assert.Equal("Hallo schöne Welt\nzweite Zeile", loaded.OcrText);
        Assert.Equal(json, CaptureDocumentSerializer.Serialize(loaded));
        // erkannt, aber kein Text: "" bleibt "" (null hieße „keine Erkennung“)
        doc.OcrText = "";
        Assert.Equal("", CaptureDocumentSerializer.Deserialize(CaptureDocumentSerializer.Serialize(doc)).OcrText);
    }

    [Fact]
    public void Serializer_OldDocumentWithoutOcr_Loads() {
        string json = """
            { "formatVersion": 1, "image": "a.png", "virtualScreen": { "x": 0, "y": 0, "width": 10, "height": 10 }, "annotations": [], "ocrText": null }
            """;
        CaptureDocument loaded = CaptureDocumentSerializer.Deserialize(json);
        Assert.Null(loaded.OcrText);
        string written = CaptureDocumentSerializer.Serialize(loaded);
        Assert.DoesNotContain("\"ocr\"", written);
        Assert.Contains("\"ocrText\": null", written);
    }

    /// <summary>Vorabversionen von 0.15 schrieben ein Objekt <c>ocr</c>: lesbar, aber nicht als unbekanntes Feld aufheben.</summary>
    [Theory]
    [InlineData("\"ocr\": { \"region\": { \"x\": 0, \"y\": 0, \"width\": 10, \"height\": 10 }, \"language\": \"de-DE\", \"lines\": [ { \"text\": \"a\", \"bounds\": { \"x\": 1, \"y\": 1, \"width\": 2, \"height\": 2 }, \"words\": [ { \"text\": \"a\", \"bounds\": { \"x\": 1, \"y\": 1, \"width\": 2, \"height\": 2 } } ] } ] }")]
    [InlineData("\"ocr\": \"text aus der Zukunft\"")]
    [InlineData("\"ocr\": [1, 2]")]
    [InlineData("\"ocr\": null")]
    public void Serializer_OcrObjectFromPreRelease_IsReadButNotWrittenAgain(string field) {
        string json = "{ \"image\": \"a.png\", " + field + ", \"ocrText\": \"a\", \"comment\": \"bleibt\", \"neuesFeld\": 7 }";
        CaptureDocument loaded = CaptureDocumentSerializer.Deserialize(json);
        Assert.Equal("bleibt", loaded.Comment);
        Assert.Equal("a", loaded.OcrText);
        string written = CaptureDocumentSerializer.Serialize(loaded);
        Assert.DoesNotContain("\"ocr\"", written);
        Assert.DoesNotContain("\"words\"", written);
        // andere unbekannte Felder bleiben wie bisher erhalten
        Assert.Contains("\"neuesFeld\": 7", written);
    }

    // ---------- Koordinaten ----------

    [Fact]
    public void OcrLayout_Scale_UpscalesSmall_DownscalesHuge_KeepsLarge() {
        Assert.Equal(2, OcrLayout.GetScale(400, 100, 10000));
        Assert.Equal(2, OcrLayout.GetScale(1920, 1040, 10000));
        // über 2 Megapixel: unverändert
        Assert.Equal(1, OcrLayout.GetScale(3840, 1080, 10000));
        // Verdoppeln würde die Grenze überschreiten
        Assert.Equal(1, OcrLayout.GetScale(6000, 10, 10000));
        // größer als die Grenze: auf die längere Seite verkleinern
        Assert.Equal(0.5, OcrLayout.GetScale(20000, 1080, 10000));
        Assert.Equal(1, OcrLayout.GetScale(0, 10, 10000));
        Assert.Equal((10000, 540), OcrLayout.GetScaledSize(20000, 1080, 0.5, 10000));
    }

    [Fact]
    public void OcrLayout_ToImage_DividesByScale_AddsRegionOrigin_RoundsOutward() {
        PixelRect region = new PixelRect(100, 50, 300, 200);
        // im doppelt großen Ausschnitt bei (20, 10) mit 41×21 → Bild (110, 55), außen gerundet
        Assert.Equal(new PixelRect(110, 55, 21, 11), OcrLayout.ToImage(20, 10, 41, 21, region, 2));
        // Faktor 1: nur verschoben
        Assert.Equal(new PixelRect(103, 54, 10, 8), OcrLayout.ToImage(3, 4, 10, 8, region, 1));
        // verkleinerter Ausschnitt (Faktor 0,5)
        Assert.Equal(new PixelRect(120, 70, 40, 20), OcrLayout.ToImage(10, 10, 20, 10, region, 0.5));
        // über den Rand hinaus → auf den Ausschnitt begrenzt
        Assert.Equal(new PixelRect(390, 240, 10, 10), OcrLayout.ToImage(290, 190, 50, 50, region, 1));
        // getrennte Faktoren je Achse
        Assert.Equal(new PixelRect(110, 60, 5, 5), OcrLayout.ToImage(20, 30, 10, 15, region, 2, 3));
    }

    [Fact]
    public void OcrLayout_ScaleBgra_KeepsUniformColor_AndSize() {
        byte[] pixels = new byte[3 * 2 * 4];
        for(int i = 0; i < pixels.Length; i += 4) {
            pixels[i] = 10;
            pixels[i + 1] = 20;
            pixels[i + 2] = 30;
            pixels[i + 3] = 255;
        }
        Assert.Same(pixels, OcrLayout.ScaleBgra(pixels, 3, 2, 3, 2));
        byte[] scaled = OcrLayout.ScaleBgra(pixels, 3, 2, 6, 4);
        Assert.Equal(6 * 4 * 4, scaled.Length);
        for(int i = 0; i < scaled.Length; i += 4) {
            Assert.Equal(new byte[] { 10, 20, 30, 255 }, scaled[i..(i + 4)]);
        }
    }

    [Fact]
    public void OcrLayout_Union_IgnoresEmpty() {
        Assert.Equal(new PixelRect(5, 5, 20, 15), OcrLayout.Union(new[] { new PixelRect(5, 10, 5, 5), default, new PixelRect(20, 5, 5, 15) }));
        Assert.Equal(default, OcrLayout.Union(Array.Empty<PixelRect>()));
    }

    // ---------- Wortauswahl ----------

    [Fact]
    public void TextSelection_NearestWord_PrefersLineBand_ThenHorizontalDistance() {
        OcrTextSelection selection = new OcrTextSelection(sample());
        Assert.Equal(5, selection.WordCount);
        Assert.Equal(1, selection.FindNearestWord(60, 15));
        // zwischen zwei Wörtern: das waagerecht nähere
        Assert.Equal(1, selection.FindNearestWord(93, 15));
        Assert.Equal(2, selection.FindNearestWord(97, 15));
        // links vor der Zeile → erstes Wort, rechts dahinter → letztes
        Assert.Equal(3, selection.FindNearestWord(0, 35));
        Assert.Equal(4, selection.FindNearestWord(190, 35));
        // zwischen den Zeilen bzw. darunter: nächstgelegene Zeile
        Assert.Equal(0, selection.FindNearestWord(12, 22));
        Assert.Equal(4, selection.FindNearestWord(80, 90));
        Assert.Null(new OcrTextSelection(null).FindNearestWord(1, 1));
    }

    [Fact]
    public void TextSelection_RangeAcrossLines_InReadingOrder() {
        OcrTextSelection selection = new OcrTextSelection(sample());
        Assert.False(selection.HasSelection);
        Assert.Equal("", selection.GetSelectedText());
        // rückwärts gezogen: Reihenfolge egal
        selection.SelectRange(3, 1);
        Assert.Equal("schöne Welt\r\nzweite", selection.GetSelectedText());
        Assert.Equal("schöne Welt\nzweite", selection.GetSelectedText("\n"));
        IReadOnlyList<PixelRect> highlights = selection.GetHighlightRects();
        Assert.Equal(2, highlights.Count);
        Assert.Equal(new PixelRect(50, 10, 80, 10), highlights[0]);
        Assert.Equal(new PixelRect(10, 30, 40, 10), highlights[1]);

        selection.SelectAll();
        Assert.Equal("Hallo schöne Welt\r\nzweite Zeile", selection.GetSelectedText());
        selection.Clear();
        Assert.False(selection.HasSelection);
        Assert.Empty(selection.GetHighlightRects());
    }

    [Fact]
    public void TextSelection_WholeLine_UsesRecognizedLineText() {
        RecognizedText text = sample();
        // die Erkennung liefert den Zeilentext ggf. anders als die mit Leerzeichen verbundenen Wörter (z. B. Japanisch)
        text.Lines[1].Text = "zweite  Zeile";
        OcrTextSelection selection = new OcrTextSelection(text);
        selection.SelectRange(3, 4);
        Assert.Equal("zweite  Zeile", selection.GetSelectedText());
        selection.SelectRange(4, 4);
        Assert.Equal("Zeile", selection.GetSelectedText());
    }

    [Fact]
    public void TextSelection_WordAt_UsesTolerance() {
        OcrTextSelection selection = new OcrTextSelection(sample());
        Assert.Equal(2, selection.FindWordAt(110, 15));
        Assert.Null(selection.FindWordAt(95, 15));
        Assert.Equal(1, selection.FindWordAt(92, 15, 3));
        Assert.Null(selection.FindWordAt(5, 50, 3));
    }

    [Fact]
    public void TextSelection_EmptyOrMissingResult_DoesNothing() {
        OcrTextSelection none = new OcrTextSelection(null);
        none.SelectAll();
        none.SelectRange(0, 3);
        Assert.False(none.HasSelection);
        OcrTextSelection empty = new OcrTextSelection(new RecognizedText());
        empty.SelectAll();
        Assert.False(empty.HasSelection);
        Assert.Null(empty.FindWordAt(1, 1, 100));
    }

    // ---------- Werkzeug „Text auswählen“ ----------

    [Fact]
    public void TextSelectTool_DragSelectsWords_ViewStateOnly() {
        Sta.Run(() => {
            EditorViewModel vm = createEditor(document());
            // Ergebnis nur im Speicher des Editors (nicht im Dokument)
            Assert.True(vm.ApplyOcrResult(sample()));
            Assert.True(vm.SelectToolByKey(TextSelectTool.ToolKey));
            Assert.True(vm.TextSelectTool.HasText);
            Assert.Equal(TextSelectTool.ToolKey, ToolShortcuts.GetToolKey(Key.I));
            Assert.Equal(EditorCursor.IBeam, vm.GetCursor(at(5, 5)));

            vm.PointerDown(at(55, 15, 1));
            Assert.True(vm.IsGestureActive);
            vm.PointerMove(at(40, 25));
            vm.PointerMove(at(30, 35));
            vm.PointerUp(at(30, 35));
            Assert.False(vm.IsGestureActive);
            Assert.Equal("schöne Welt" + Environment.NewLine + "zweite", vm.SelectedRecognizedText);
            Assert.Equal(2, vm.GetTextHighlights().Count);
            // reiner Ansichtszustand: kein Objekt, kein Rückgängig-Schritt, Dokument unverändert
            Assert.Empty(vm.Document.Annotations);
            Assert.False(vm.UndoStack.CanUndo);

            // Klick ohne Ziehen hebt die Markierung auf
            vm.PointerDown(at(60, 15, 1));
            vm.PointerUp(at(60, 15));
            Assert.Equal("", vm.SelectedRecognizedText);

            // Doppelklick markiert ein Wort
            vm.PointerDown(at(105, 15, 2));
            vm.PointerUp(at(105, 15));
            Assert.Equal("Welt", vm.SelectedRecognizedText);

            Assert.True(vm.TextSelectTool.SelectAll());
            Assert.Equal("Hallo schöne Welt" + Environment.NewLine + "zweite Zeile", vm.SelectedRecognizedText);

            // anderes Werkzeug: Markierung weg, keine Hervorhebung
            vm.SelectTool(vm.SelectionTool);
            Assert.Equal("", vm.SelectedRecognizedText);
            Assert.Empty(vm.GetTextHighlights());
            Assert.False(vm.TextSelectTool.HasSelection);
        });
    }

    [Fact]
    public void TextSelectTool_WithoutOcr_DoesNothing_AndCopyFallsBack() {
        Sta.Run(() => {
            EditorViewModel vm = createEditor(document());
            Assert.True(vm.SelectToolByKey(TextSelectTool.ToolKey));
            vm.PointerDown(at(10, 10, 1));
            vm.PointerMove(at(100, 50));
            vm.PointerUp(at(100, 50));
            vm.PointerDown(at(10, 10, 2));
            vm.PointerUp(at(10, 10));
            Assert.False(vm.TextSelectTool.SelectAll());
            Assert.False(vm.TextSelectTool.HasText);
            Assert.Equal("", vm.SelectedRecognizedText);
            // ohne Markierung kopiert Strg+C wie bisher das Bild (der Text-Weg lehnt ab)
            Assert.False(vm.CopySelectedRecognizedText());
            Assert.False(vm.ShowOptions);
        });
    }

    [Fact]
    public void TextSelectTool_NewOcrResult_ResetsSelection() {
        Sta.Run(() => {
            EditorViewModel vm = createEditor(document());
            vm.ApplyOcrResult(sample());
            vm.SelectToolByKey(TextSelectTool.ToolKey);
            vm.TextSelectTool.SelectAll();
            Assert.True(vm.TextSelectTool.HasSelection);
            Assert.True(vm.ApplyOcrResult(sample()));
            Assert.False(vm.TextSelectTool.HasSelection);
        });
    }

    [Fact]
    public void Editor_OcrTarget_FollowsSelection_AndRejectsStaleResults() {
        Sta.Run(() => {
            EditorViewModel vm = createEditor(document());
            Assert.Equal(new PixelRect(0, 0, 200, 100), vm.OcrTargetRegion);
            Assert.True(vm.NeedsOcr);
            Assert.True(vm.ApplyOcrResult(sample()));
            Assert.False(vm.NeedsOcr);
            Assert.Equal("Hallo schöne Welt\nzweite Zeile", vm.Document.OcrText);
            Assert.False(vm.UndoStack.CanUndo);

            vm.SetSelection(new PixelRect(10, 10, 50, 40));
            Assert.Equal(new PixelRect(10, 10, 50, 40), vm.OcrTargetRegion);
            Assert.True(vm.NeedsOcr);
            // Ergebnis für den alten Ausschnitt wird nicht übernommen
            Assert.False(vm.ApplyOcrResult(sample()));
            Assert.Equal(new PixelRect(0, 0, 200, 100), vm.RecognizedText!.Region);
            // beim Schließen jetzt: Text passt nicht zum Bereich → Hintergrund-Erkennung
            Assert.True(vm.NeedsOcrTextOnClose);
            RecognizedText empty = new RecognizedText { Region = new PixelRect(10, 10, 50, 40), Language = "de-DE" };
            Assert.True(vm.ApplyOcrResult(empty));
            // erkannt, aber kein Text: "" (null hieße „keine Erkennung“)
            Assert.Equal("", vm.Document.OcrText);
            Assert.False(vm.NeedsOcrTextOnClose);

            // Rückgängig: wieder ganzes Bild → neue Erkennung nötig
            vm.Undo();
            Assert.True(vm.NeedsOcr);
            Assert.True(vm.NeedsOcrTextOnClose);
        });
    }

    [Fact]
    public void Editor_OpenedWithStoredText_RecognizesAgain_ButKeepsTextOnUnchangedClose() {
        Sta.Run(() => {
            CaptureDocument doc = document();
            doc.Selection = new PixelRect(10, 10, 50, 40);
            doc.OcrText = "gespeichert";
            EditorViewModel vm = createEditor(doc);
            // Lage der Wörter gibt es nach dem Öffnen nicht → im Editor neu erkennen
            Assert.Null(vm.RecognizedText);
            Assert.True(vm.NeedsOcr);
            // der gespeicherte Text gehört zum gespeicherten Bereich: unverändert schließen braucht keine Erkennung
            Assert.False(vm.NeedsOcrTextOnClose);
            vm.SetSelection(new PixelRect(0, 0, 20, 20));
            Assert.True(vm.NeedsOcrTextOnClose);
            vm.Undo();
            Assert.False(vm.NeedsOcrTextOnClose);

            // ohne gespeicherten Text (neue Aufnahme) muss beim Schließen erkannt werden
            Assert.True(createEditor(document()).NeedsOcrTextOnClose);
        });
    }

    private static EditorViewModel editorWithStoredText(string? text) {
        CaptureDocument doc = document();
        doc.Selection = new PixelRect(10, 10, 50, 40);
        doc.OcrText = text;
        return createEditor(doc);
    }

    [Fact]
    public void OcrOff_StoredTextAfterSelectionChange_IsRecognizedAgain() {
        Sta.Run(() => {
            EditorViewModel vm = editorWithStoredText("alt");
            // ausgeschaltet, Bereich unverändert: keine Erkennung, Text bleibt
            Assert.False(vm.ShouldRecognize(false));
            Assert.False(vm.PrepareOcrTextForClose(false));
            Assert.Equal("alt", vm.Document.OcrText);
            // eingeschaltet: beim Öffnen immer (Lage der Wörter fehlt im Speicher)
            Assert.True(vm.ShouldRecognize(true));

            vm.SetSelection(new PixelRect(0, 0, 100, 50));
            Assert.True(vm.HasStaleOcrText);
            Assert.True(vm.ShouldRecognize(false));
            // Rückgängig auf den gespeicherten Bereich: Text passt wieder
            vm.Undo();
            Assert.False(vm.ShouldRecognize(false));
            vm.Redo();

            // neue Erkennung gelungen: Text ersetzt, nichts mehr zu tun
            RecognizedText result = new RecognizedText { Region = new PixelRect(0, 0, 100, 50), Lines = sample().Lines };
            Assert.True(vm.ApplyOcrResult(result));
            Assert.Equal("Hallo schöne Welt\nzweite Zeile", vm.Document.OcrText);
            Assert.False(vm.ShouldRecognize(false));
            Assert.False(vm.PrepareOcrTextForClose(false));
        });
    }

    [Fact]
    public void OcrOff_FailedRecognitionOfStaleText_ClearsIt() {
        Sta.Run(() => {
            EditorViewModel vm = editorWithStoredText("alt");
            vm.SetSelection(new PixelRect(0, 0, 100, 50));
            // eingeschaltet bleibt bei Fehlschlag alles, wie es war
            vm.OnOcrFailed(true);
            Assert.Equal("alt", vm.Document.OcrText);
            vm.OnOcrFailed(false);
            Assert.Null(vm.Document.OcrText);
            // danach (ohne Text, ausgeschaltet) keine Erkennung mehr
            Assert.False(vm.ShouldRecognize(false));
            Assert.False(vm.PrepareOcrTextForClose(false));
        });
    }

    [Fact]
    public void OcrOff_CloseWithStaleText_RemovesItBeforeSaving_AndRequestsBackgroundRecognition() {
        Sta.Run(() => {
            EditorViewModel vm = editorWithStoredText("alt");
            vm.SetSelection(new PixelRect(0, 0, 100, 50));
            Assert.True(vm.PrepareOcrTextForClose(false));
            // gespeichert wird null – auch wenn die Hintergrund-Erkennung danach fehlschlägt, steht kein alter Text mehr da
            Assert.Null(vm.Document.OcrText);

            // eingeschaltet: wie bisher, der Text wird erst von der Hintergrund-Erkennung ersetzt
            EditorViewModel on = editorWithStoredText("alt");
            on.SetSelection(new PixelRect(0, 0, 100, 50));
            Assert.True(on.PrepareOcrTextForClose(true));
            Assert.Equal("alt", on.Document.OcrText);

            // ausgeschaltet und ohne Text: wie bisher keine Erkennung
            EditorViewModel none = editorWithStoredText(null);
            none.SetSelection(new PixelRect(0, 0, 100, 50));
            Assert.False(none.ShouldRecognize(false));
            Assert.False(none.PrepareOcrTextForClose(false));
            none.OnOcrFailed(false);
            Assert.Null(none.Document.OcrText);
        });
    }

    // ---------- Zuschnitt: Dateinamen und Ablage ----------

    [Fact]
    public void CaptureFiles_CropPath_NextToOriginal() {
        CaptureFiles files = CaptureFiles.FromJson(@"C:\x\2026\11\2026-11-17_09-00-00.json");
        Assert.Equal(@"C:\x\2026\11\2026-11-17_09-00-00.crop.png", files.CropPath);
        Assert.Equal(files.CropPath, CaptureFiles.FromPng(@"C:\x\2026\11\2026-11-17_09-00-00.png").CropPath);
        Assert.Equal(4, files.AllPaths.Count());
        Assert.Contains(files.CropPath, files.AllPaths);
        Assert.Equal("2026-11-17_09-00-00", CaptureFiles.GetBaseName(files.CropPath));
        Assert.True(CaptureFiles.IsCropFile(files.CropPath));
        Assert.True(CaptureFiles.IsCropFile("A.CROP.PNG"));
        Assert.False(CaptureFiles.IsCropFile(files.PngPath));
        Assert.False(CaptureFiles.IsCropFile("crop.png"));
    }

    [Fact]
    public void Storage_ReadFolder_IgnoresCropFiles_UniqueNameCountsThem() {
        using (TempFolder temp = new TempFolder()) {
            File.WriteAllText(Path.Combine(temp.Path, "2026-11-17_09-00-00.png"), "");
            File.WriteAllText(Path.Combine(temp.Path, "2026-11-17_09-00-00.crop.png"), "");
            File.WriteAllText(Path.Combine(temp.Path, "2026-11-17_09-00-01.crop.png"), "");
            List<CaptureEntry> entries = StorageService.ReadFolder(temp.Path, AppSettings.DefaultFileNamePattern);
            Assert.Equal("2026-11-17_09-00-00", Assert.Single(entries).Name);
            // ein übrig gebliebener Zuschnitt belegt den Namen
            Assert.Equal("2026-11-17_09-00-01_2", StorageService.GetUniqueBaseName(temp.Path, "2026-11-17_09-00-01"));
        }
    }

    [Fact]
    public void Storage_CropRegion_IsSelectionClippedToImage_OrWholeImage() {
        CaptureDocument doc = document();
        Assert.Equal(new PixelRect(0, 0, 200, 100), StorageService.GetCropRegion(doc, 200, 100));
        doc.Selection = new PixelRect(150, 50, 100, 100);
        Assert.Equal(new PixelRect(150, 50, 50, 50), StorageService.GetCropRegion(doc, 200, 100));
        doc.Selection = new PixelRect(500, 500, 10, 10);
        Assert.Equal(new PixelRect(0, 0, 200, 100), StorageService.GetCropRegion(doc, 200, 100));
    }

    private static StorageService createStorage(TempFolder temp, bool saveCrop) {
        SettingsService settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
        settings.Load();
        settings.Settings.StorageFolder = Path.Combine(temp.Path, "ablage");
        settings.Settings.SaveCropImage = saveCrop;
        return new StorageService(settings, new RenderService());
    }

    [Fact]
    public void Storage_SaveCropOff_WritesNoCrop() {
        using (TempFolder temp = new TempFolder()) {
            Sta.Run(() => {
                StorageService storage = createStorage(temp, false);
                SavedCapture saved = storage.SaveNewCapture(document(), TestImages.CreatePattern(200, 100));
                Task.WaitAll(saved.PngTask, saved.ThumbnailTask, saved.CropTask);
                Assert.True(saved.CropTask.IsCompletedSuccessfully);
                Assert.False(File.Exists(saved.Files.CropPath));
            });
        }
    }

    /// <summary>Rendert über RenderTargetBitmap – scheitert wie die RenderServiceTests in einer getrennten RDP-Sitzung.</summary>
    [Fact]
    public void Storage_SaveNewCapture_WritesCrop_AndUpdateThumbnailRewritesIt() {
        using (TempFolder temp = new TempFolder()) {
            Sta.Run(() => {
                StorageService storage = createStorage(temp, true);
                BitmapSource original = TestImages.CreatePattern(200, 100);
                CaptureDocument doc = document();
                SavedCapture saved = storage.SaveNewCapture(doc, original);
                Task.WaitAll(saved.PngTask, saved.ThumbnailTask, saved.CropTask);
                BitmapSource crop = ImageFiles.LoadBgra32(saved.Files.CropPath);
                Assert.Equal(200, crop.PixelWidth);
                Assert.Equal(100, crop.PixelHeight);
                // nicht als eigene Aufnahme gezählt
                Assert.Single(storage.EnumerateCaptures());

                doc.Selection = new PixelRect(20, 10, 60, 30);
                storage.UpdateThumbnail(doc, original, saved.Files.JsonPath);
                storage.WaitForBackgroundWrites(TimeSpan.FromSeconds(30));
                crop = ImageFiles.LoadBgra32(saved.Files.CropPath);
                Assert.Equal(60, crop.PixelWidth);
                Assert.Equal(30, crop.PixelHeight);
                Assert.Equal(TestImages.GetPixels(new CroppedBitmap(original, new System.Windows.Int32Rect(20, 10, 60, 30))), TestImages.GetPixels(crop));
            });
        }
    }

    [Fact]
    public void Storage_Duplicate_CopiesCrop() {
        using (TempFolder temp = new TempFolder()) {
            Sta.Run(() => {
                StorageService storage = createStorage(temp, false);
                SavedCapture saved = storage.SaveNewCapture(document(), TestImages.CreatePattern(200, 100));
                Task.WaitAll(saved.PngTask, saved.ThumbnailTask);
                File.WriteAllText(saved.Files.CropPath, "crop");
                CaptureFiles copy = storage.DuplicateCapture(saved.Files.JsonPath);
                Assert.Equal("crop", File.ReadAllText(copy.CropPath));
            });
        }
    }

    [Fact]
    public void Storage_SetRecognizedText_OnlyWhenRegionStillMatches() {
        using (TempFolder temp = new TempFolder()) {
            Sta.Run(() => {
                StorageService storage = createStorage(temp, false);
                CaptureDocument doc = document();
                SavedCapture saved = storage.SaveNewCapture(doc, TestImages.CreatePattern(200, 100));
                Task.WaitAll(saved.PngTask, saved.ThumbnailTask);

                Assert.False(storage.SetRecognizedText(saved.Files.JsonPath, sample(new PixelRect(1, 1, 10, 10)), 200, 100));
                Assert.Null(storage.LoadDocument(saved.Files.JsonPath).OcrText);

                Assert.True(storage.SetRecognizedText(saved.Files.JsonPath, sample(), 200, 100));
                CaptureDocument loaded = storage.LoadDocument(saved.Files.JsonPath);
                Assert.Equal("Hallo schöne Welt\nzweite Zeile", loaded.OcrText);
                // nur der Text, keine Lage der Wörter
                string json = File.ReadAllText(saved.Files.JsonPath);
                Assert.DoesNotContain("\"ocr\"", json);
                Assert.DoesNotContain("\"words\"", json);

                // gelöschte bzw. verworfene Aufnahme: nichts neu anlegen
                Assert.False(storage.SetRecognizedText(Path.Combine(temp.Path, "fehlt.json"), sample(), 200, 100));
                Assert.False(File.Exists(Path.Combine(temp.Path, "fehlt.json")));
            });
        }
    }

    // ---------- Suche ----------

    [Fact]
    public void HistoryFilter_FindsOcrText_AlsoAcrossLineBreaks() {
        CaptureDocument doc = document();
        doc.OcrText = "Speichern fehlgeschlagen\nZugriff verweigert";
        CaptureEntry entry = new CaptureEntry(CaptureFiles.FromJson(@"C:\x\a.json"), new DateTime(2026, 11, 17)) {
            Metadata = CaptureMetadata.FromDocument(doc)
        };
        Assert.Equal(doc.OcrText, entry.Metadata.OcrText);
        Assert.True(new HistoryFilter(null, null, "zugriff").Matches(entry));
        Assert.True(new HistoryFilter(null, null, "fehlgeschlagen zugriff").Matches(entry));
        Assert.False(new HistoryFilter(null, null, "Passwort").Matches(entry));
        Assert.Equal("", CaptureMetadata.FromDocument(document()).OcrText);
    }

    // ---------- Einstellungen ----------

    [Fact]
    public void Settings_CropAndOcr_DefaultOn_AlsoForOldFiles() {
        using (TempFolder temp = new TempFolder()) {
            string path = Path.Combine(temp.Path, "settings.json");
            SettingsService fresh = new SettingsService(path);
            fresh.Load();
            Assert.True(fresh.Settings.SaveCropImage);
            Assert.True(fresh.Settings.RecognizeText);

            // Datei aus 0.14 ohne die Felder → Standard „an“
            File.WriteAllText(path, "{ \"settingsVersion\": 1, \"magnifierZoom\": 9 }");
            SettingsService old = new SettingsService(path);
            old.Load();
            Assert.True(old.Settings.SaveCropImage);
            Assert.True(old.Settings.RecognizeText);

            old.Settings.SaveCropImage = false;
            old.Settings.RecognizeText = false;
            old.Save();
            string written = File.ReadAllText(path);
            Assert.Contains("\"saveCropImage\": false", written);
            Assert.Contains("\"recognizeText\": false", written);
            SettingsService reloaded = new SettingsService(path);
            reloaded.Load();
            Assert.False(reloaded.Settings.SaveCropImage);
            Assert.False(reloaded.Settings.RecognizeText);
        }
    }
}
