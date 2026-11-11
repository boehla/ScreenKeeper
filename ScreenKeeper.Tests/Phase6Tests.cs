using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels;

namespace ScreenKeeper.Tests;

/// <summary>
/// Feinschliff (Phase 6): Monitore links vom primären (negative Koordinaten), DPI 150 %, Tastatur-Hook-Filter,
/// verlustfreies Datenformat, Speichern im Hintergrund und Sonderfälle der Ablage.
/// </summary>
public class Phase6Tests {
    // Zwei Full-HD-Monitore, der zweite links vom primären: virtueller Bildschirm beginnt bei x = -1920
    private static readonly PixelRect virtualScreen = new PixelRect(-1920, 0, 3840, 1080);

    private static CaptureDocument createTwoMonitorDocument(double scale = 1.0) {
        return new CaptureDocument {
            CapturedAt = new DateTimeOffset(2026, 11, 9, 14, 32, 5, TimeSpan.FromHours(1)),
            VirtualScreen = virtualScreen,
            Monitors = new List<MonitorInfo> {
                new MonitorInfo { Device = @"\\.\DISPLAY1", Bounds = new PixelRect(1920, 0, 1920, 1080), Primary = true, Scale = scale },
                new MonitorInfo { Device = @"\\.\DISPLAY2", Bounds = new PixelRect(0, 0, 1920, 1080), Primary = false, Scale = scale }
            }
        };
    }

    [Fact]
    public void ScreenCoordinates_NegativeVirtualScreen() {
        // linker Monitor in Bildschirmkoordinaten (-1920,0)-(0,1080) → Bildpixel (0,0) 1920×1080
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), ScreenCoordinates.ScreenToImage(-1920, 0, 0, 1080, virtualScreen));
        Assert.Equal(new PixelRect(1920, 0, 1920, 1080), ScreenCoordinates.ScreenToImage(0, 0, 1920, 1080, virtualScreen));
        // Fenster, das über beide Monitore reicht
        Assert.Equal(new PixelRect(1820, 50, 300, 200), ScreenCoordinates.ScreenToImage(-100, 50, 200, 250, virtualScreen));

        Assert.Equal(new PixelPoint(1919, 500), ScreenCoordinates.ScreenToImage(-1, 500, virtualScreen));
        Assert.Equal(new PixelPoint(1920, 0), ScreenCoordinates.ScreenToImage(0, 0, virtualScreen));
        Assert.Equal(new PixelPoint(-1920, 0), ScreenCoordinates.ImageToScreen(new PixelPoint(0, 0), virtualScreen));
        Assert.Equal(new PixelPoint(-1, 1079), ScreenCoordinates.ImageToScreen(new PixelPoint(1919, 1079), virtualScreen));
        // Hin und zurück (Pfeiltasten setzen den echten Mauszeiger, die Maus liefert wieder denselben Bildpixel)
        for(int x = 1915; x <= 1925; x++) {
            PixelPoint screen = ScreenCoordinates.ImageToScreen(new PixelPoint(x, 7), virtualScreen);
            Assert.Equal(new PixelPoint(x, 7), ScreenCoordinates.ScreenToImage(screen.X, screen.Y, virtualScreen));
        }

        // Monitor oberhalb des primären: negative Y-Koordinaten
        PixelRect stacked = new PixelRect(0, -1080, 1920, 2160);
        Assert.Equal(new PixelRect(0, 0, 1920, 1080), ScreenCoordinates.ScreenToImage(0, -1080, 1920, 0, stacked));
        Assert.Equal(new PixelPoint(10, -1070), ScreenCoordinates.ImageToScreen(new PixelPoint(10, 10), stacked));
    }

    [Fact]
    public void TwoMonitors_MonitorLookupToolbarAndMagnifierStayOnTheirMonitor() {
        CaptureDocument document = createTwoMonitorDocument();
        // aktueller Monitor (Zwischenablage) in Bildpixeln
        Assert.Equal(1, SelectionGeometry.FindMonitorIndex(document.Monitors, new PixelPoint(100, 100)));
        Assert.Equal(0, SelectionGeometry.FindMonitorIndex(document.Monitors, new PixelPoint(1920, 100)));
        Assert.Equal(1, SelectionGeometry.FindMonitorIndex(document.Monitors, new PixelPoint(1919, 1079)));
        Assert.Equal(0, SelectionGeometry.FindPrimaryMonitorIndex(document.Monitors));

        ViewTransform transform = new ViewTransform { Zoom = 1 };
        Rect primary = transform.ToView(document.Monitors[0].Bounds);
        Rect left = transform.ToView(document.Monitors[1].Bounds);

        // Werkzeugleiste ohne Bereich: oben mittig auf dem primären Monitor (rechts, ab Bildpixel 1920)
        Size toolbar = new Size(600, 40);
        Point toolbarPosition = ToolbarPlacement.Place(null, toolbar, primary, 10);
        Assert.Equal(1920 + (1920 - 600) / 2.0, toolbarPosition.X);
        Assert.True(toolbarPosition.X + toolbar.Width <= primary.Right);

        // Bereich ganz rechts unten auf dem linken Monitor: Leiste bleibt auf dem linken Monitor
        Rect selection = transform.ToView(new PixelRect(1700, 1000, 219, 79));
        Point below = ToolbarPlacement.Place(selection, toolbar, left, 10);
        Assert.True(below.X + toolbar.Width <= left.Right && below.Y + toolbar.Height <= left.Bottom && below.X >= left.Left);

        // Lupe am rechten Rand des linken Monitors weicht nach links aus und ragt nicht auf den primären Monitor
        Size lens = new Size(200, 230);
        Point magnifier = MagnifierPlacement.Place(new Point(1915, 500), lens, left, 26);
        Assert.True(magnifier.X + lens.Width <= left.Right);
        Assert.True(magnifier.X + lens.Width <= 1915);
    }

    [Fact]
    public void Dpi150_FullscreenZoomMapsDevicePixelsOneToOne() {
        CaptureDocument document = createTwoMonitorDocument(1.5);
        double deviceScale = 1.5;
        ViewTransform transform = new ViewTransform { Zoom = 1.0 / deviceScale };

        // Monitor in DIPs: physisch 1920 breit = 1280 DIPs
        Rect primary = transform.ToView(document.Monitors[0].Bounds);
        Assert.Equal(1280, primary.Left, 6);
        Assert.Equal(1280, primary.Width, 6);
        Assert.Equal(720, primary.Height, 6);

        // Maus: physische Bildschirmposition → DIPs im Fenster (Fensterursprung = virtueller Bildschirm) → Bildpixel
        for(int screenX = -3; screenX <= 3; screenX++) {
            Point view = new Point((screenX - virtualScreen.X) / deviceScale, (500 - virtualScreen.Y) / deviceScale);
            PixelPoint pixel = transform.ToPixel(view);
            Assert.Equal(ScreenCoordinates.ScreenToImage(screenX, 500, virtualScreen), pixel);
        }
        // Mitte eines Bildpixels (Pfeiltasten) liegt im selben Pixel
        Point center = transform.ToView(1919 + 0.5, 0.5);
        Assert.Equal(new PixelPoint(1919, 0), transform.ToPixel(center));

        // Lupe in DIPs bleibt im (DIP-)Monitor
        Rect left = transform.ToView(document.Monitors[1].Bounds);
        Size lens = new Size(150, 170);
        Point magnifier = MagnifierPlacement.Place(new Point(1275, 700), lens, left, 26 / deviceScale);
        Assert.True(magnifier.X >= left.Left && magnifier.X + lens.Width <= left.Right);
        Assert.True(magnifier.Y >= left.Top && magnifier.Y + lens.Height <= left.Bottom);
    }

    [Fact]
    public void IsSameLayout_ComparesVirtualScreenMonitorsAndScale() {
        CaptureDocument document = createTwoMonitorDocument();
        List<MonitorInfo> current = new List<MonitorInfo> {
            new MonitorInfo { Bounds = new PixelRect(0, 0, 1920, 1080), Scale = 1.0 },
            new MonitorInfo { Bounds = new PixelRect(1920, 0, 1920, 1080), Scale = 1.0, Primary = true }
        };
        Assert.True(ScreenCoordinates.IsSameLayout(virtualScreen, current, document));
        // linker Monitor abgesteckt
        Assert.False(ScreenCoordinates.IsSameLayout(new PixelRect(0, 0, 1920, 1080), current.Take(1).ToList(), document));
        // gleicher virtueller Bildschirm, aber nach rechts gewandert (Ursprung 0 statt -1920)
        Assert.False(ScreenCoordinates.IsSameLayout(new PixelRect(0, 0, 3840, 1080), current, document));
        // andere Skalierung
        List<MonitorInfo> scaled = current.Select(m => new MonitorInfo { Bounds = m.Bounds, Scale = 1.5 }).ToList();
        Assert.False(ScreenCoordinates.IsSameLayout(virtualScreen, scaled, document));
    }

    [Fact]
    public void HotkeyFilter_SuppressesOnlyConfiguredCombinationsIncludingKeyUp() {
        // wie der frühere PrintScreenFilter: fehlgeschlagene Druck-Kombinationen gelten immer
        HotkeyFilter filter = new HotkeyFilter(new[] {
            new HookBinding(HotkeyAction.CaptureEdit, new HotkeyBinding(HotkeyModifiers.None, HotkeyBinding.VkPrintScreen), HotkeyScope.Always),
            new HookBinding(HotkeyAction.CaptureSave, new HotkeyBinding(HotkeyModifiers.Ctrl, HotkeyBinding.VkPrintScreen), HotkeyScope.Always)
        });

        Assert.True(filter.Process(HotkeyBinding.VkPrintScreen, true, HotkeyModifiers.None, out HotkeyAction? action));
        Assert.Equal(HotkeyAction.CaptureEdit, action);
        // automatische Wiederholung: unterdrücken, aber nicht erneut auslösen
        Assert.True(filter.Process(HotkeyBinding.VkPrintScreen, true, HotkeyModifiers.None, out action));
        Assert.Null(action);
        Assert.True(filter.Process(HotkeyBinding.VkPrintScreen, false, HotkeyModifiers.None, out action));
        Assert.Null(action);

        // Strg+Druck, Strg vor der Druck-Taste losgelassen: das Loslassen wird trotzdem unterdrückt
        Assert.True(filter.Process(HotkeyBinding.VkPrintScreen, true, HotkeyModifiers.Ctrl, out action));
        Assert.Equal(HotkeyAction.CaptureSave, action);
        Assert.True(filter.Process(HotkeyBinding.VkPrintScreen, false, HotkeyModifiers.None, out action));

        // nicht konfigurierte Kombination und andere Tasten gehen durch
        Assert.False(filter.Process(HotkeyBinding.VkPrintScreen, true, HotkeyModifiers.Alt, out action));
        Assert.Null(action);
        Assert.False(filter.Process(HotkeyBinding.VkPrintScreen, false, HotkeyModifiers.None, out action));
        Assert.False(filter.Process(0x48, true, HotkeyModifiers.Ctrl | HotkeyModifiers.Alt, out action));
        Assert.Null(action);

        // nach dem Loslassen löst der nächste Druck wieder aus
        Assert.True(filter.Process(HotkeyBinding.VkPrintScreen, true, HotkeyModifiers.None, out action));
        Assert.Equal(HotkeyAction.CaptureEdit, action);
    }

    private const string specExample = """
        {
          "formatVersion": 1,
          "capturedAt": "2026-11-09T14:32:05+01:00",
          "image": "2026-11-09_14-32-05.png",
          "virtualScreen": { "x": -1920, "y": 0, "width": 3840, "height": 1080 },
          "monitors": [
            { "device": "\\\\.\\DISPLAY1", "bounds": { "x": 1920, "y": 0, "width": 1920, "height": 1080 }, "primary": true,  "scale": 1.0 },
            { "device": "\\\\.\\DISPLAY2", "bounds": { "x": 0,    "y": 0, "width": 1920, "height": 1080 }, "primary": false, "scale": 1.0 }
          ],
          "windows": [
            { "title": "Rechner", "process": "CalculatorApp", "bounds": { "x": 100, "y": 80, "width": 400, "height": 600 }, "z": 0 }
          ],
          "cursor": { "png": "<base64>", "x": 512, "y": 300, "hotspotX": 0, "hotspotY": 0, "capturedVisible": true },
          "selection": { "x": 80, "y": 60, "width": 640, "height": 480 },
          "annotations": [
            { "id": "3f2a…", "type": "arrow", "points": [[100,100],[220,180]], "stroke": "#FFE53935", "thickness": 4 },
            { "id": "8c1d…", "type": "pixelate", "rect": { "x": 300, "y": 200, "width": 120, "height": 30 }, "blockSize": 10 },
            { "id": "a7b0…", "type": "cursor", "x": 540, "y": 320, "kind": "captured" }
          ],
          "ocrText": null
        }
        """;

    [Fact]
    public void SpecExample_RoundtripIsLossless() {
        CaptureDocument document = CaptureDocumentSerializer.Deserialize(specExample);
        string saved = CaptureDocumentSerializer.Serialize(document);
        using (JsonDocument original = JsonDocument.Parse(specExample)) {
            using (JsonDocument written = JsonDocument.Parse(saved)) {
                // Jeder Wert des Beispiels steht unverändert in der gespeicherten Datei (dazu kommen nur Standardwerte wie
                // "visible": true), auch "ocrText": null und die Reihenfolge der Annotationen
                assertContained(original.RootElement, written.RootElement, "$");
                Assert.Equal(JsonValueKind.Null, written.RootElement.GetProperty("ocrText").ValueKind);
                string[] ids = written.RootElement.GetProperty("annotations").EnumerateArray().Select(a => a.GetProperty("id").GetString()!).ToArray();
                Assert.Equal(new[] { "3f2a…", "8c1d…", "a7b0…" }, ids);
            }
        }
        // zweite Runde ist identisch
        Assert.Equal(saved, CaptureDocumentSerializer.Serialize(CaptureDocumentSerializer.Deserialize(saved)));
    }

    [Fact]
    public void UnknownFieldsOnKnownObjects_ArePreserved() {
        string json = """
            {
              "formatVersion": 3,
              "capturedAt": "2026-11-09T14:32:05+01:00",
              "image": "a.png",
              "virtualScreen": { "x": -1920, "y": 0, "width": 3840, "height": 1080 },
              "monitors": [ { "device": "D", "bounds": { "x": 0, "y": 0, "width": 10, "height": 10 }, "primary": true, "scale": 1.0, "hdr": true } ],
              "windows": [ { "title": "T", "process": "p", "bounds": { "x": 0, "y": 0, "width": 5, "height": 5 }, "z": 0, "hwnd": 1234 } ],
              "cursor": { "png": "AA==", "x": 1, "y": 2, "hotspotX": 0, "hotspotY": 0, "capturedVisible": true, "shape": "arrow" },
              "annotations": [
                { "id": "1", "type": "rectangle", "rect": { "x": 1, "y": 2, "width": 3, "height": 4 }, "stroke": "#FF000000", "thickness": 2, "shadow": { "blur": 4 } },
                { "id": "2", "type": "sticker", "emoji": "x" }
              ],
              "tags": [ "a", "b" ]
            }
            """;
        CaptureDocument document = CaptureDocumentSerializer.Deserialize(json);
        Assert.Equal(3, document.FormatVersion);
        RectangleAnnotation rectangle = Assert.IsType<RectangleAnnotation>(document.Annotations[0]);
        // Kopie (Rückgängig, Duplizieren) behält die unbekannten Felder ebenfalls
        Assert.True(rectangle.Clone().ExtensionData!.ContainsKey("shadow"));
        Assert.False(rectangle.ExtensionData!.ContainsKey("type"));

        string saved = CaptureDocumentSerializer.Serialize(document);
        using (JsonDocument original = JsonDocument.Parse(json)) {
            using (JsonDocument written = JsonDocument.Parse(saved)) {
                assertContained(original.RootElement, written.RootElement, "$");
                int typeCount = written.RootElement.GetProperty("annotations")[0].EnumerateObject().Count(p => p.Name == "type");
                Assert.Equal(1, typeCount);
            }
        }
    }

    [Fact]
    public void BrokenOrEmptyJson_ThrowsReadableError() {
        Assert.ThrowsAny<Exception>(() => CaptureDocumentSerializer.Deserialize(""));
        Assert.ThrowsAny<Exception>(() => CaptureDocumentSerializer.Deserialize("{ \"formatVersion\": 1, "));
        Assert.Throws<InvalidDataException>(() => CaptureDocumentSerializer.Deserialize("null"));
        Assert.ThrowsAny<Exception>(() => CaptureDocumentSerializer.Deserialize("{ \"annotations\": 5 }"));
    }

    [Fact]
    public void SaveNewCapture_PatternWithoutSeconds_GetsSuffixAndThumbnailUpdateWaitsForBackgroundWrite() {
        using (TempFolder temp = new TempFolder()) {
            Sta.Run(() => {
                SettingsService settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
                settings.Load();
                settings.Settings.StorageFolder = Path.Combine(temp.Path, "ablage");
                settings.Settings.FileNamePattern = "{yyyy}-{MM}-{dd}_{HH}-{mm}";
                StorageService storage = new StorageService(settings, new RenderService());
                BitmapSource original = TestImages.CreatePattern(1200, 300);
                DateTimeOffset time = new DateTimeOffset(2026, 11, 9, 14, 32, 5, TimeSpan.FromHours(1));

                CaptureDocument first = new CaptureDocument { CapturedAt = time, VirtualScreen = new PixelRect(0, 0, 1200, 300) };
                SavedCapture firstSaved = storage.SaveNewCapture(first, original);
                // sofort danach (Editor sofort wieder geschlossen): darf nicht mit dem Hintergrund-Vorschaubild kollidieren
                first.Selection = new PixelRect(10, 10, 100, 50);
                storage.UpdateThumbnail(first, original, firstSaved.Files.JsonPath);

                CaptureDocument second = new CaptureDocument { CapturedAt = time.AddSeconds(20), VirtualScreen = first.VirtualScreen };
                SavedCapture secondSaved = storage.SaveNewCapture(second, original);
                Task.WaitAll(firstSaved.PngTask, firstSaved.ThumbnailTask, secondSaved.PngTask, secondSaved.ThumbnailTask);

                Assert.EndsWith("_14-32.json", firstSaved.Files.JsonPath);
                Assert.EndsWith("_14-32_2.json", secondSaved.Files.JsonPath);
                // das Vorschaubild zeigt den Bereich (100 × 50), nicht das ganze Bild – das spätere Schreiben hat gewonnen
                BitmapSource thumb = ImageFiles.LoadBgra32(firstSaved.Files.ThumbPath);
                Assert.Equal(100, thumb.PixelWidth);
                Assert.Equal(50, thumb.PixelHeight);
                Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(firstSaved.Files.JsonPath)!, "*.tmp"));
            });
        }
    }

    [Fact]
    public void SaveNewCapture_FolderOnMissingDrive_ThrowsInsteadOfCrashing() {
        string? missing = Enumerable.Range('D', 23).Select(c => $"{(char)c}:\\").FirstOrDefault(root => !Directory.Exists(root));
        if(missing == null) return;
        using (TempFolder temp = new TempFolder()) {
            Sta.Run(() => {
                SettingsService settings = new SettingsService(Path.Combine(temp.Path, "settings.json"));
                settings.Load();
                settings.Settings.StorageFolder = Path.Combine(missing, "ScreenKeeper");
                StorageService storage = new StorageService(settings, new RenderService());
                CaptureDocument document = new CaptureDocument { CapturedAt = DateTimeOffset.Now, VirtualScreen = new PixelRect(0, 0, 10, 10) };
                Assert.ThrowsAny<IOException>(() => storage.SaveNewCapture(document, TestImages.CreatePattern(10, 10)));
                Assert.Empty(storage.EnumerateCaptures());
            });
        }
    }

    [Fact]
    public void Thumbnail_OfTwoFourKMonitors() {
        Sta.Run(() => {
            // 2 × 4K = 7680 × 2160 (66 MB Bgra32): keine Überläufe, Vorschaubild höchstens 320 px breit
            BitmapSource original = TestImages.CreatePattern(7680, 2160);
            BitmapSource copy = ImageFiles.ToBgra32(original);
            Assert.Equal(7680, copy.PixelWidth);
            BitmapSource thumb = StorageService.CreateThumbnail(original);
            Assert.Equal(320, thumb.PixelWidth);
            Assert.Equal(90, thumb.PixelHeight);
        });
    }

    /// <summary>
    /// Alles aus <paramref name="expected"/> steht gleichwertig in <paramref name="actual"/> (Objekte dürfen zusätzliche
    /// Eigenschaften haben, Arrays müssen gleich lang und gleich geordnet sein, Zahlen werden numerisch verglichen).
    /// </summary>
    private static void assertContained(JsonElement expected, JsonElement actual, string path) {
        switch(expected.ValueKind) {
            case JsonValueKind.Object:
                Assert.True(actual.ValueKind == JsonValueKind.Object, $"{path}: Objekt erwartet");
                foreach(JsonProperty property in expected.EnumerateObject()) {
                    Assert.True(actual.TryGetProperty(property.Name, out JsonElement value), $"{path}.{property.Name} fehlt");
                    assertContained(property.Value, value, $"{path}.{property.Name}");
                }
                return;
            case JsonValueKind.Array:
                Assert.True(actual.ValueKind == JsonValueKind.Array, $"{path}: Array erwartet");
                Assert.Equal(expected.GetArrayLength(), actual.GetArrayLength());
                for(int i = 0; i < expected.GetArrayLength(); i++) assertContained(expected[i], actual[i], $"{path}[{i}]");
                return;
            case JsonValueKind.Number:
                Assert.True(actual.ValueKind == JsonValueKind.Number, $"{path}: Zahl erwartet");
                Assert.Equal(expected.GetDouble(), actual.GetDouble());
                return;
            default:
                Assert.True(expected.ValueKind == actual.ValueKind, $"{path}: {expected.ValueKind} erwartet, {actual.ValueKind} gefunden");
                if(expected.ValueKind == JsonValueKind.String) Assert.Equal(expected.GetString(), actual.GetString());
                return;
        }
    }
}
