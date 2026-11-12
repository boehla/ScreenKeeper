using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using Xunit.Abstractions;

namespace ScreenKeeper.Tests;

/// <summary>
/// 0.11.0: Textmarker im Modus „Multiplizieren“ statt halbtransparenter Überlagerung. Untergrund ist das, was tatsächlich
/// zu sehen ist (Original plus vorher gezeichnete Objekte) – unter einer Verpixelung scheint nie das Original durch.
/// Pixeltests über den Export brauchen RenderTargetBitmap und damit eine aktive Sitzung.
/// </summary>
public class HighlightMultiplyTests {
    private readonly ITestOutputHelper output;

    public HighlightMultiplyTests(ITestOutputHelper output) {
        this.output = output;
    }

    /// <summary>Ein deckendes Pbgra32-Pixel (B, G, R, A) durch den Marker schicken.</summary>
    private static byte[] multiplyPixel(byte red, byte green, byte blue, Color color) {
        return ImageEffects.Multiply(new byte[] { blue, green, red, 255 }, 1, 1, color.R, color.G, color.B, color.A);
    }

    private static BitmapSource solid(int width, int height, byte red, byte green, byte blue) {
        byte[] pixels = new byte[width * height * 4];
        for(int i = 0; i < pixels.Length; i += 4) {
            pixels[i] = blue;
            pixels[i + 1] = green;
            pixels[i + 2] = red;
            pixels[i + 3] = 255;
        }
        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    private static byte[] render(BitmapSource original, params Annotation[] annotations) {
        CaptureDocument document = new CaptureDocument { Annotations = annotations.ToList() };
        return TestImages.GetPixels(new RenderService().Render(document, original, new PixelRect(0, 0, original.PixelWidth, original.PixelHeight)));
    }

    /// <summary>(R, G, B) des Pixels (x, y) in einem Puffer der Breite <paramref name="width"/>.</summary>
    private static (int R, int G, int B) pixelAt(byte[] pixels, int width, int x, int y) {
        int i = (y * width + x) * 4;
        return (pixels[i + 2], pixels[i + 1], pixels[i]);
    }

    private static void assertClose((int R, int G, int B) expected, (int R, int G, int B) actual, int tolerance = 1) {
        bool close = Math.Abs(expected.R - actual.R) <= tolerance && Math.Abs(expected.G - actual.G) <= tolerance && Math.Abs(expected.B - actual.B) <= tolerance;
        Assert.True(close, $"erwartet {expected}, ist {actual}");
    }

    private bool skipWithoutSession() {
        if(RenderCheck.IsRenderTargetBitmapWorking()) return false;
        output.WriteLine("Übersprungen: RenderTargetBitmap liefert ohne aktive Sitzung leere Pixel.");
        return true;
    }

    [Fact]
    public void Multiply_WhiteGivesMarkerColor_BlackStaysBlack_GreyIsTinted() {
        Color yellow = RenderService.DefaultHighlightColor;
        byte[] white = multiplyPixel(255, 255, 255, yellow);
        Assert.Equal(new byte[] { 0x3B, 0xEB, 0xFF, 255 }, white);
        byte[] black = multiplyPixel(0, 0, 0, yellow);
        Assert.Equal(new byte[] { 0, 0, 0, 255 }, black);
        byte[] grey = multiplyPixel(240, 240, 240, yellow);
        assertClose((240, 221, 56), (grey[2], grey[1], grey[0]));
        Assert.Equal(255, grey[3]);
    }

    [Fact]
    public void Multiply_ColorAlphaActsAsStrength() {
        // halbe Stärke auf Weiß: halb zwischen Weiß und Gelb
        byte[] half = multiplyPixel(255, 255, 255, Color.FromArgb(128, 0xFF, 0xEB, 0x3B));
        assertClose((255, 245, 157), (half[2], half[1], half[0]));
        // Alpha 0 ändert nichts, Alpha 255 ist volles Multiplizieren
        Assert.Equal(new byte[] { 10, 100, 200, 255 }, multiplyPixel(200, 100, 10, Color.FromArgb(0, 0xFF, 0xEB, 0x3B)));
        byte[] full = multiplyPixel(200, 100, 10, Color.FromArgb(255, 0x80, 0x80, 0x80));
        assertClose((100, 50, 5), (full[2], full[1], full[0]));
    }

    [Fact]
    public void Multiply_OnTransparentBackdrop_ShowsMarkerColor() {
        Color yellow = RenderService.DefaultHighlightColor;
        byte[] result = ImageEffects.Multiply(new byte[] { 0, 0, 0, 0 }, 1, 1, yellow.R, yellow.G, yellow.B, yellow.A);
        Assert.Equal(new byte[] { 0x3B, 0xEB, 0xFF, 255 }, result);
    }

    [Fact]
    public void Export_HighlightOnWhite_IsExactMarkerColor_OutsideUnchanged() {
        Sta.Run(() => {
            if(skipWithoutSession()) return;
            BitmapSource original = solid(60, 40, 255, 255, 255);
            byte[] pixels = render(original, new HighlightAnnotation { Rect = new PixelRect(10, 10, 30, 10) });
            Assert.Equal((0xFF, 0xEB, 0x3B), pixelAt(pixels, 60, 10, 10));
            Assert.Equal((0xFF, 0xEB, 0x3B), pixelAt(pixels, 60, 39, 19));
            Assert.Equal((255, 255, 255), pixelAt(pixels, 60, 9, 10));
            Assert.Equal((255, 255, 255), pixelAt(pixels, 60, 40, 19));
            Assert.Equal((255, 255, 255), pixelAt(pixels, 60, 10, 20));

            // dunkler Text bleibt dunkel, Grau wird getönt
            BitmapSource grey = solid(20, 20, 240, 240, 240);
            assertClose((240, 221, 56), pixelAt(render(grey, new HighlightAnnotation { Rect = new PixelRect(0, 0, 20, 20) }), 20, 5, 5));
            BitmapSource black = solid(20, 20, 0, 0, 0);
            Assert.Equal((0, 0, 0), pixelAt(render(black, new HighlightAnnotation { Rect = new PixelRect(0, 0, 20, 20) }), 20, 5, 5));
        });
    }

    [Fact]
    public void Export_HighlightOverPixelate_ShowsPixelatedNotOriginal() {
        Sta.Run(() => {
            if(skipWithoutSession()) return;
            const int size = 80;
            BitmapSource original = TestImages.CreatePattern(size, size);
            byte[] source = TestImages.GetPixels(original);
            PixelateAnnotation pixelate = new PixelateAnnotation { Rect = new PixelRect(10, 10, 50, 40), BlockSize = 10 };
            HighlightAnnotation highlight = new HighlightAnnotation { Rect = new PixelRect(5, 15, 60, 20) };
            byte[] pixelated = render(original, pixelate);
            byte[] marked = render(original, pixelate, highlight);
            Color yellow = RenderService.DefaultHighlightColor;
            int differsFromOriginal = 0;
            int count = 0;
            for(int y = 15; y < 35; y++) {
                for(int x = 10; x < 60; x++) {
                    (int R, int G, int B) actual = pixelAt(marked, size, x, y);
                    (int R, int G, int B) under = pixelAt(pixelated, size, x, y);
                    byte[] expected = multiplyPixel((byte)under.R, (byte)under.G, (byte)under.B, yellow);
                    assertClose((expected[2], expected[1], expected[0]), actual);
                    (int R, int G, int B) plain = pixelAt(source, size, x, y);
                    byte[] fromOriginal = multiplyPixel((byte)plain.R, (byte)plain.G, (byte)plain.B, yellow);
                    if((fromOriginal[2], fromOriginal[1], fromOriginal[0]) != actual) differsFromOriginal++;
                    count++;
                }
            }
            // Das Original darf nicht durchscheinen (ein Mittelwert kann zufällig einem Originalwert gleichen)
            Assert.True(differsFromOriginal > count * 19 / 20, $"nur {differsFromOriginal} von {count} Pixeln weichen vom markierten Original ab");
            // außerhalb der Verpixelung, aber im Marker: Original multipliziert
            (int R, int G, int B) left = pixelAt(source, size, 6, 20);
            byte[] leftExpected = multiplyPixel((byte)left.R, (byte)left.G, (byte)left.B, yellow);
            assertClose((leftExpected[2], leftExpected[1], leftExpected[0]), pixelAt(marked, size, 6, 20));
        });
    }

    [Fact]
    public void Export_OpaqueRectangleBelowHighlight_StaysVisibleTinted() {
        Sta.Run(() => {
            if(skipWithoutSession()) return;
            BitmapSource original = solid(60, 60, 255, 255, 255);
            RectangleAnnotation rectangle = new RectangleAnnotation { Rect = new PixelRect(10, 10, 30, 30), Fill = "#FF2060FF", Stroke = "#FF2060FF", Thickness = 2 };
            HighlightAnnotation highlight = new HighlightAnnotation { Rect = new PixelRect(0, 20, 60, 10) };
            byte[] pixels = render(original, rectangle, highlight);
            // Rechteck unter dem Marker: Blau getönt, nicht Gelb
            assertClose((32, 88, 59), pixelAt(pixels, 60, 25, 25));
            // daneben (Weiß unter dem Marker): Markerfarbe
            Assert.Equal((0xFF, 0xEB, 0x3B), pixelAt(pixels, 60, 50, 25));
            // Rechteck außerhalb des Markers unverändert
            Assert.Equal((0x20, 0x60, 0xFF), pixelAt(pixels, 60, 25, 15));
        });
    }

    [Fact]
    public void Export_OverlappingHighlights_UpperMultipliesLower() {
        Sta.Run(() => {
            if(skipWithoutSession()) return;
            BitmapSource original = solid(60, 30, 255, 255, 255);
            byte[] pixels = render(original,
                new HighlightAnnotation { Rect = new PixelRect(0, 0, 40, 30) },
                new HighlightAnnotation { Rect = new PixelRect(20, 0, 40, 30) });
            Assert.Equal((0xFF, 0xEB, 0x3B), pixelAt(pixels, 60, 10, 10));
            assertClose((255, 0xEB * 0xEB / 255, 0x3B * 0x3B / 255), pixelAt(pixels, 60, 30, 10));
            Assert.Equal((0xFF, 0xEB, 0x3B), pixelAt(pixels, 60, 50, 10));
        });
    }

    [Fact]
    public void Highlight_PartlyOutsideImage_IsClipped_FullyOutsideDrawsNothing() {
        Sta.Run(() => {
            BitmapSource original = TestImages.CreatePattern(100, 100);
            RenderService render = new RenderService();
            CaptureDocument partly = new CaptureDocument {
                Annotations = new List<Annotation> {
                    new HighlightAnnotation { Rect = new PixelRect(-10, -10, 30, 30) },
                    new PixelateAnnotation { Rect = new PixelRect(80, 80, 40, 40) },
                    new HighlightAnnotation { Rect = new PixelRect(90, 90, 50, 50) }
                }
            };
            Assert.Equal(new Rect(0, 0, 100, 100), render.BuildAnnotationDrawing(partly, original).Bounds);
            render.Render(partly, original, new PixelRect(0, 0, 100, 100));
            CaptureDocument outside = new CaptureDocument { Annotations = new List<Annotation> { new HighlightAnnotation { Rect = new PixelRect(200, 200, 20, 20) } } };
            Assert.True(render.BuildAnnotationDrawing(outside, original).Bounds.IsEmpty);
        });
    }

    [Fact]
    public void HighlightOnOriginal_IsCached_HighlightOverObjects_IsNot() {
        Sta.Run(() => {
            BitmapSource original = TestImages.CreatePattern(100, 100);
            RenderService render = new RenderService();
            HighlightAnnotation highlight = new HighlightAnnotation { Rect = new PixelRect(10, 10, 30, 10) };
            CaptureDocument document = new CaptureDocument { Annotations = new List<Annotation> { highlight } };
            render.BuildAnnotationDrawing(document, original);
            Assert.Equal(1, render.CachedEffectCount);
            // andere Farbe: neuer Eintrag, der alte fällt weg
            highlight.Color = "#FF4CAF50";
            render.BuildAnnotationDrawing(document, original);
            Assert.Equal(1, render.CachedEffectCount);
            // ein Rahmen darunter: Untergrund aus Original + Rahmen, nicht zwischengespeichert
            document.Annotations.Insert(0, new RectangleAnnotation { Rect = new PixelRect(0, 0, 50, 50) });
            render.BuildAnnotationDrawing(document, original);
            Assert.Equal(0, render.CachedEffectCount);
            // ein Objekt darüber ist für den Marker egal
            document.Annotations.RemoveAt(0);
            document.Annotations.Add(new RectangleAnnotation { Rect = new PixelRect(0, 0, 50, 50) });
            render.BuildAnnotationDrawing(document, original);
            Assert.Equal(1, render.CachedEffectCount);
        });
    }
}
