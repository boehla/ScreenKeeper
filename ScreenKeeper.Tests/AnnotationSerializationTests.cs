using System.Text.Json;
using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.Tests;

public class AnnotationSerializationTests {
    private static CaptureDocument createFullDocument() {
        return new CaptureDocument {
            Image = "a.png",
            VirtualScreen = new PixelRect(0, 0, 100, 100),
            Annotations = new List<Annotation> {
                new RectangleAnnotation { Rect = new PixelRect(1, 2, 3, 4), Stroke = "#FF010203", Thickness = 6, Fill = "#FF010203" },
                new EllipseAnnotation { Rect = new PixelRect(5, 6, 7, 8), Thickness = 1 },
                new LineAnnotation { Points = new List<PixelPoint> { new PixelPoint(1, 1), new PixelPoint(9, 9) }, Thickness = 2 },
                new ArrowAnnotation { Points = new List<PixelPoint> { new PixelPoint(2, 2), new PixelPoint(8, 3) }, Thickness = 12 },
                new FreehandAnnotation { Points = new List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(3, 4), new PixelPoint(6, 1) }, Smooth = false },
                new TextAnnotation { X = 4, Y = 5, Text = "Zeile 1\r\nZeile 2 – äöü", FontSize = 32, Foreground = "#FF000000", Background = "#F0FFFFFF", Width = 120, Bold = true },
                new HighlightAnnotation { Rect = new PixelRect(10, 10, 20, 5), Color = "#FF00FF00" },
                new PixelateAnnotation { Rect = new PixelRect(0, 0, 50, 50), BlockSize = 14 },
                new BlurAnnotation { Rect = new PixelRect(5, 5, 30, 30), Radius = 16 },
                new NumberStampAnnotation { X = 40, Y = 40, Number = 7, Size = 48, Color = "#FF1E88E5" },
                new CursorAnnotation { X = 3, Y = 3, Kind = CursorKinds.IBeam, Visible = false }
            }
        };
    }

    [Fact]
    public void AllAnnotationTypes_RoundTripUnchanged() {
        CaptureDocument document = createFullDocument();
        string json = CaptureDocumentSerializer.Serialize(document);
        CaptureDocument loaded = CaptureDocumentSerializer.Deserialize(json);
        Assert.Equal(json, CaptureDocumentSerializer.Serialize(loaded));
        Assert.Equal(document.Annotations.Select(a => a.Type), loaded.Annotations.Select(a => a.Type));
        Assert.Equal(AnnotationListConverter.KnownTypes.OrderBy(t => t), loaded.Annotations.Select(a => a.Type).Distinct().OrderBy(t => t));

        TextAnnotation text = Assert.IsType<TextAnnotation>(loaded.Annotations[5]);
        Assert.Equal(120, text.Width);
        Assert.Equal("Zeile 1\r\nZeile 2 – äöü", text.Text);
        Assert.True(text.Bold);
        Assert.False(Assert.IsType<FreehandAnnotation>(loaded.Annotations[4]).Smooth);
        Assert.False(Assert.IsType<CursorAnnotation>(loaded.Annotations[10]).Visible);
        Assert.Equal(16, Assert.IsType<BlurAnnotation>(loaded.Annotations[8]).Radius);
    }

    [Fact]
    public void TextWidth_IsOmittedWhenNull_AndReadAsNull() {
        string json = CaptureDocumentSerializer.Serialize(new CaptureDocument { Annotations = new List<Annotation> { new TextAnnotation { Text = "x" } } });
        Assert.DoesNotContain("\"width\": null", json);
        using (JsonDocument parsed = JsonDocument.Parse(json)) {
            JsonElement text = parsed.RootElement.GetProperty("annotations")[0];
            Assert.False(text.TryGetProperty("width", out _));
            Assert.Equal("text", text.GetProperty("type").GetString());
        }
        CaptureDocument loaded = CaptureDocumentSerializer.Deserialize(json);
        Assert.Null(((TextAnnotation)loaded.Annotations[0]).Width);
    }

    [Fact]
    public void IsEdited_CountsDrawingsButNotCapturedCursor() {
        CaptureDocument document = new CaptureDocument { Annotations = new List<Annotation> { new CursorAnnotation { Kind = CursorKinds.Captured } } };
        Assert.False(document.IsEdited);
        document.Annotations.Add(new CursorAnnotation { Kind = CursorKinds.Hand });
        Assert.True(document.IsEdited);
    }

    [Fact]
    public void NewSettingsFields_HaveDefaults_AndAreNormalized() {
        using (TempFolder temp = new TempFolder()) {
            string path = Path.Combine(temp.Path, "settings.json");
            File.WriteAllText(path, "{ \"defaultFontSize\": 1, \"defaultBlockSize\": 0, \"defaultBlurRadius\": 999, \"defaultStampSize\": 2, \"defaultCursorKind\": \"gibtsnicht\", \"defaultFill\": true }");
            SettingsService service = new SettingsService(path);
            service.Load();
            AppSettings settings = service.Settings;
            AppSettings defaults = new AppSettings();
            Assert.Equal(defaults.DefaultFontSize, settings.DefaultFontSize);
            Assert.Equal(defaults.DefaultBlockSize, settings.DefaultBlockSize);
            Assert.Equal(defaults.DefaultBlurRadius, settings.DefaultBlurRadius);
            Assert.Equal(defaults.DefaultStampSize, settings.DefaultStampSize);
            Assert.Equal(CursorKinds.Arrow, settings.DefaultCursorKind);
            Assert.True(settings.DefaultFill);
            Assert.True(settings.DefaultSmooth);
            Assert.False(settings.DefaultTextBackground);

            settings.DefaultFontSize = 48;
            settings.DefaultCursorKind = CursorKinds.Captured;
            service.Save();
            SettingsService reloaded = new SettingsService(path);
            reloaded.Load();
            Assert.Equal(48, reloaded.Settings.DefaultFontSize);
            Assert.Equal(CursorKinds.Captured, reloaded.Settings.DefaultCursorKind);
        }
    }
}
