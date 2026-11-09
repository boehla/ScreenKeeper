using System.Text.Json;
using ScreenKeeper.Models;

namespace ScreenKeeper.Tests;

public class CaptureDocumentSerializerTests {
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
    public void SpecExample_LoadsWithExpectedValues() {
        CaptureDocument doc = CaptureDocumentSerializer.Deserialize(specExample);

        Assert.Equal(1, doc.FormatVersion);
        Assert.Equal(new DateTimeOffset(2026, 11, 9, 14, 32, 5, TimeSpan.FromHours(1)), doc.CapturedAt);
        Assert.Equal("2026-11-09_14-32-05.png", doc.Image);
        Assert.Equal(new PixelRect(-1920, 0, 3840, 1080), doc.VirtualScreen);

        Assert.Equal(2, doc.Monitors.Count);
        Assert.Equal(@"\\.\DISPLAY1", doc.Monitors[0].Device);
        Assert.Equal(new PixelRect(1920, 0, 1920, 1080), doc.Monitors[0].Bounds);
        Assert.True(doc.Monitors[0].Primary);
        Assert.False(doc.Monitors[1].Primary);
        Assert.Equal(1.0, doc.Monitors[1].Scale);

        WindowInfo window = Assert.Single(doc.Windows);
        Assert.Equal("Rechner", window.Title);
        Assert.Equal("CalculatorApp", window.Process);
        Assert.Equal(new PixelRect(100, 80, 400, 600), window.Bounds);
        Assert.Equal(0, window.Z);

        Assert.NotNull(doc.Cursor);
        Assert.Equal("<base64>", doc.Cursor!.Png);
        Assert.Equal(512, doc.Cursor.X);
        Assert.Equal(300, doc.Cursor.Y);
        Assert.True(doc.Cursor.CapturedVisible);

        Assert.Equal(new PixelRect(80, 60, 640, 480), doc.Selection);
        Assert.Null(doc.OcrText);

        Assert.Equal(3, doc.Annotations.Count);
        ArrowAnnotation arrow = Assert.IsType<ArrowAnnotation>(doc.Annotations[0]);
        Assert.Equal("3f2a…", arrow.Id);
        Assert.Equal(new[] { new PixelPoint(100, 100), new PixelPoint(220, 180) }, arrow.Points);
        Assert.Equal("#FFE53935", arrow.Stroke);
        Assert.Equal(4, arrow.Thickness);

        PixelateAnnotation pixelate = Assert.IsType<PixelateAnnotation>(doc.Annotations[1]);
        Assert.Equal(new PixelRect(300, 200, 120, 30), pixelate.Rect);
        Assert.Equal(10, pixelate.BlockSize);

        CursorAnnotation cursor = Assert.IsType<CursorAnnotation>(doc.Annotations[2]);
        Assert.Equal(540, cursor.X);
        Assert.Equal(320, cursor.Y);
        Assert.Equal(CursorKinds.Captured, cursor.Kind);
        Assert.True(cursor.Visible);
    }

    [Fact]
    public void FullDocument_RoundtripKeepsEverything() {
        CaptureDocument doc = createFullDocument();

        string json = CaptureDocumentSerializer.Serialize(doc);
        CaptureDocument loaded = CaptureDocumentSerializer.Deserialize(json);
        string again = CaptureDocumentSerializer.Serialize(loaded);

        Assert.Equal(json, again);
        Assert.Equal(doc.Annotations.Count, loaded.Annotations.Count);
        for(int i = 0; i < doc.Annotations.Count; i++) {
            Assert.Equal(doc.Annotations[i].GetType(), loaded.Annotations[i].GetType());
            Assert.Equal(doc.Annotations[i].Id, loaded.Annotations[i].Id);
        }
        Assert.Equal("Fenster mit Ümläut", loaded.Windows[0].Title);
        Assert.Contains("Ümläut", json);

        // Diskriminator steht in jedem Objekt und es gibt keine doppelte "type"-Eigenschaft
        using (JsonDocument parsed = JsonDocument.Parse(json)) {
            foreach(JsonElement element in parsed.RootElement.GetProperty("annotations").EnumerateArray()) {
                int typeCount = element.EnumerateObject().Count(p => p.Name == "type");
                Assert.Equal(1, typeCount);
            }
        }
    }

    [Fact]
    public void AllAnnotationTypes_AreRegistered() {
        Type[] types = typeof(Annotation).Assembly.GetTypes()
            .Where(t => t.IsSubclassOf(typeof(Annotation)) && !t.IsAbstract && t != typeof(UnknownAnnotation))
            .ToArray();
        Assert.Equal(11, types.Length);
        Assert.Equal(11, AnnotationListConverter.KnownTypes.Count);
        foreach(Type type in types) {
            Annotation instance = (Annotation)Activator.CreateInstance(type)!;
            Assert.Contains(instance.Type, AnnotationListConverter.KnownTypes);
        }
    }

    [Fact]
    public void UnknownAnnotationType_IsPreservedOnRoundtrip() {
        string json = """
            {
              "formatVersion": 1,
              "capturedAt": "2026-11-09T14:32:05+01:00",
              "image": "a.png",
              "virtualScreen": { "x": 0, "y": 0, "width": 100, "height": 100 },
              "annotations": [
                { "id": "1", "type": "rectangle", "rect": { "x": 1, "y": 2, "width": 3, "height": 4 }, "stroke": "#FF000000", "thickness": 2 },
                { "type": "hologram", "id": "zukunft", "depth": 3.5, "layers": [ { "a": 1 }, [1, 2] ], "note": "aus Version 9" },
                { "id": "3", "type": "cursor", "x": 5, "y": 6, "kind": "hand", "visible": false }
              ],
              "futureField": { "x": 1 }
            }
            """;

        CaptureDocument doc = CaptureDocumentSerializer.Deserialize(json);
        Assert.Equal(3, doc.Annotations.Count);
        Assert.IsType<RectangleAnnotation>(doc.Annotations[0]);
        UnknownAnnotation unknown = Assert.IsType<UnknownAnnotation>(doc.Annotations[1]);
        Assert.Equal("hologram", unknown.Type);
        Assert.Equal("zukunft", unknown.Id);
        CursorAnnotation cursor = Assert.IsType<CursorAnnotation>(doc.Annotations[2]);
        Assert.False(cursor.Visible);
        Assert.Equal(CursorKinds.Hand, cursor.Kind);

        string saved = CaptureDocumentSerializer.Serialize(doc);
        using (JsonDocument original = JsonDocument.Parse(json)) {
            using (JsonDocument written = JsonDocument.Parse(saved)) {
                JsonElement originalUnknown = original.RootElement.GetProperty("annotations")[1];
                JsonElement writtenUnknown = written.RootElement.GetProperty("annotations")[1];
                Assert.True(JsonElement.DeepEquals(originalUnknown, writtenUnknown));
                Assert.True(written.RootElement.TryGetProperty("futureField", out _));
            }
        }
    }

    [Fact]
    public void Save_IsAtomicAndLoadable() {
        using (TempFolder temp = new TempFolder()) {
            string path = Path.Combine(temp.Path, "doc.json");
            CaptureDocument doc = createFullDocument();
            CaptureDocumentSerializer.Save(doc, path);
            CaptureDocumentSerializer.Save(doc, path);

            Assert.False(File.Exists(path + ".tmp"));
            CaptureDocument loaded = CaptureDocumentSerializer.Load(path);
            Assert.Equal(doc.Annotations.Count, loaded.Annotations.Count);
            byte[] bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "JSON ohne BOM erwartet");
        }
    }

    [Fact]
    public void MissingLists_BecomeEmpty() {
        CaptureDocument doc = CaptureDocumentSerializer.Deserialize("""{ "formatVersion": 1, "monitors": null, "annotations": null }""");
        Assert.NotNull(doc.Monitors);
        Assert.NotNull(doc.Windows);
        Assert.NotNull(doc.Annotations);
        Assert.Empty(doc.Annotations);
        Assert.Null(doc.Selection);
    }

    private static CaptureDocument createFullDocument() {
        return new CaptureDocument {
            CapturedAt = new DateTimeOffset(2026, 11, 9, 14, 32, 5, TimeSpan.FromHours(1)),
            Image = "2026-11-09_14-32-05.png",
            VirtualScreen = new PixelRect(-1920, 0, 3840, 1080),
            Monitors = new List<MonitorInfo> {
                new MonitorInfo { Device = @"\\.\DISPLAY1", Bounds = new PixelRect(1920, 0, 1920, 1080), Primary = true, Scale = 1.5 },
                new MonitorInfo { Device = @"\\.\DISPLAY2", Bounds = new PixelRect(0, 0, 1920, 1080), Primary = false, Scale = 1.5 }
            },
            Windows = new List<WindowInfo> {
                new WindowInfo { Title = "Fenster mit Ümläut", Process = "notepad", Bounds = new PixelRect(10, 20, 300, 200), Z = 0 }
            },
            Cursor = new CursorInfo { Png = "AAAA", X = 5, Y = 6, HotspotX = 1, HotspotY = 2, CapturedVisible = true },
            Selection = new PixelRect(1, 2, 3, 4),
            OcrText = "Text",
            Annotations = new List<Annotation> {
                new RectangleAnnotation { Rect = new PixelRect(1, 1, 10, 10), Fill = "#80FF0000" },
                new EllipseAnnotation { Rect = new PixelRect(2, 2, 20, 10), Thickness = 2.5 },
                new LineAnnotation { Points = new List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(5, 5) } },
                new ArrowAnnotation { Points = new List<PixelPoint> { new PixelPoint(1, 2), new PixelPoint(3, 4) }, Stroke = "#FF00FF00" },
                new FreehandAnnotation { Points = new List<PixelPoint> { new PixelPoint(1, 1), new PixelPoint(2, 3), new PixelPoint(4, 2) }, Smooth = false },
                new TextAnnotation { X = 10, Y = 20, Text = "Hallo\nWelt", FontSize = 18, Bold = true, Background = "#FFFFFFFF" },
                new HighlightAnnotation { Rect = new PixelRect(5, 5, 50, 10) },
                new PixelateAnnotation { Rect = new PixelRect(6, 6, 60, 12), BlockSize = 8 },
                new BlurAnnotation { Rect = new PixelRect(7, 7, 70, 14), Radius = 5 },
                new NumberStampAnnotation { X = 30, Y = 40, Number = 3, Size = 28 },
                new CursorAnnotation { X = 100, Y = 200, Kind = CursorKinds.IBeam, Visible = false }
            }
        };
    }
}
