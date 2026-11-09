using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Services;
using Xunit.Abstractions;

namespace ScreenKeeper.Tests;

/// <summary>
/// Zeichnungen der Objekte. Die Geometrie (Bounds der DrawingGroup) lässt sich ohne Bildschirm prüfen;
/// Export-Pixeltests brauchen RenderTargetBitmap und damit eine aktive Sitzung.
/// </summary>
public class AnnotationRenderTests {
    private readonly ITestOutputHelper output;

    public AnnotationRenderTests(ITestOutputHelper output) {
        this.output = output;
    }

    private static Rect boundsOf(Annotation annotation, BitmapSource original) {
        CaptureDocument document = new CaptureDocument { Annotations = new List<Annotation> { annotation } };
        return new RenderService().BuildAnnotationDrawing(document, original).Bounds;
    }

    [Fact]
    public void Rectangle_StrokeLiesExactlyInsideRect() {
        Sta.Run(() => {
            BitmapSource original = TestImages.CreatePattern(100, 100);
            Assert.Equal(new Rect(10, 20, 30, 15), boundsOf(new RectangleAnnotation { Rect = new PixelRect(10, 20, 30, 15), Thickness = 4 }, original));
            Assert.Equal(new Rect(10, 20, 30, 15), boundsOf(new RectangleAnnotation { Rect = new PixelRect(10, 20, 30, 15), Thickness = 1 }, original));
        });
    }

    [Fact]
    public void OnePixelLine_CoversExactlyOnePixelRow() {
        Sta.Run(() => {
            BitmapSource original = TestImages.CreatePattern(100, 100);
            Rect bounds = boundsOf(new LineAnnotation { Points = new List<PixelPoint> { new PixelPoint(5, 7), new PixelPoint(15, 7) }, Thickness = 1 }, original);
            Assert.Equal(7, bounds.Top, 6);
            Assert.Equal(8, bounds.Bottom, 6);
            Assert.Equal(5, bounds.Left, 6);
            Assert.Equal(16, bounds.Right, 6);
        });
    }

    [Fact]
    public void EveryType_ProducesDrawing_AndUnknownDrawsNothing() {
        Sta.Run(() => {
            BitmapSource original = TestImages.CreatePattern(200, 200);
            List<Annotation> annotations = new List<Annotation> {
                new RectangleAnnotation { Rect = new PixelRect(10, 10, 30, 30) },
                new EllipseAnnotation { Rect = new PixelRect(10, 10, 30, 30), Fill = "#80FF0000" },
                new LineAnnotation { Points = new List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(50, 50) } },
                new ArrowAnnotation { Points = new List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(50, 50) } },
                new FreehandAnnotation { Points = new List<PixelPoint> { new PixelPoint(0, 0), new PixelPoint(20, 30), new PixelPoint(40, 0), new PixelPoint(60, 30) } },
                new TextAnnotation { X = 10, Y = 10, Text = "Hallo", Background = "#F0FFFFFF", Width = 80 },
                new HighlightAnnotation { Rect = new PixelRect(10, 10, 30, 10) },
                new PixelateAnnotation { Rect = new PixelRect(10, 10, 30, 30) },
                new BlurAnnotation { Rect = new PixelRect(10, 10, 30, 30) },
                new NumberStampAnnotation { X = 50, Y = 50, Number = 12 },
                new CursorAnnotation { X = 20, Y = 20, Kind = CursorKinds.Arrow }
            };
            foreach(Annotation annotation in annotations) {
                Rect bounds = boundsOf(annotation, original);
                Assert.False(bounds.IsEmpty, $"{annotation.Type} zeichnet nichts");
                Assert.True(bounds.Width > 0 && bounds.Height > 0, annotation.Type);
            }
            Assert.True(boundsOf(new UnknownAnnotation(System.Text.Json.JsonDocument.Parse("{\"type\":\"x\"}").RootElement), original).IsEmpty);
            Assert.True(boundsOf(new TextAnnotation { Text = "" }, original).IsEmpty);
            // Stempel: Kreis um den Mittelpunkt
            Rect stamp = boundsOf(new NumberStampAnnotation { X = 50, Y = 60, Size = 40 }, original);
            Assert.Equal(50, stamp.X + stamp.Width / 2, 3);
            Assert.Equal(60, stamp.Y + stamp.Height / 2, 3);
            // Verpixeln außerhalb des Bildes wird abgeschnitten
            Assert.Equal(new Rect(190, 190, 10, 10), boundsOf(new PixelateAnnotation { Rect = new PixelRect(190, 190, 50, 50) }, original));
        });
    }

    [Fact]
    public void EffectCache_KeepsOnlyCurrentEffects() {
        Sta.Run(() => {
            BitmapSource original = TestImages.CreatePattern(100, 100);
            RenderService render = new RenderService();
            PixelateAnnotation pixelate = new PixelateAnnotation { Rect = new PixelRect(0, 0, 40, 40) };
            BlurAnnotation blur = new BlurAnnotation { Rect = new PixelRect(50, 50, 40, 40) };
            CaptureDocument document = new CaptureDocument { Annotations = new List<Annotation> { pixelate, blur } };
            render.BuildAnnotationDrawing(document, original);
            Assert.Equal(2, render.CachedEffectCount);
            // beim Ziehen ändert sich das Rechteck: alter Eintrag fällt weg
            for(int i = 1; i <= 5; i++) {
                pixelate.Rect = new PixelRect(0, 0, 40 + i, 40);
                render.BuildAnnotationDrawing(document, original);
            }
            Assert.Equal(2, render.CachedEffectCount);
            document.Annotations.Remove(blur);
            render.BuildAnnotationDrawing(document, original);
            Assert.Equal(1, render.CachedEffectCount);
        });
    }

    [Fact]
    public void Export_PixelatedArea_ContainsNoOriginalPixels() {
        Sta.Run(() => {
            if(!RenderCheck.IsRenderTargetBitmapWorking()) {
                output.WriteLine("Übersprungen: RenderTargetBitmap liefert ohne aktive Sitzung leere Pixel.");
                return;
            }
            const int size = 80;
            BitmapSource original = TestImages.CreatePattern(size, size);
            byte[] source = TestImages.GetPixels(original);
            HashSet<int> originalColors = new HashSet<int>();
            for(int i = 0; i < source.Length; i += 4) originalColors.Add(source[i] | source[i + 1] << 8 | source[i + 2] << 16);
            PixelRect rect = new PixelRect(10, 10, 50, 40);
            CaptureDocument document = new CaptureDocument { Annotations = new List<Annotation> { new PixelateAnnotation { Rect = rect, BlockSize = 10 } } };

            byte[] result = TestImages.GetPixels(new RenderService().Render(document, original, rect));
            int originalHits = 0;
            for(int i = 0; i < result.Length; i += 4) {
                int color = result[i] | result[i + 1] << 8 | result[i + 2] << 16;
                byte[] block = result.AsSpan(i, 4).ToArray();
                if(originalColors.Contains(color)) originalHits++;
            }
            // Ein Mittelwert kann zufällig einem Originalwert gleichen; ein erkennbares Muster bleibt nicht übrig
            Assert.True(originalHits < result.Length / 4 / 20, $"{originalHits} Pixel entsprechen Originalfarben");
            // jeder 10×10-Block ist einfarbig
            for(int y = 0; y < rect.Height; y++) {
                for(int x = 0; x < rect.Width; x++) {
                    int blockStart = ((y / 10 * 10) * rect.Width + x / 10 * 10) * 4;
                    Assert.Equal(result.AsSpan(blockStart, 4).ToArray(), result.AsSpan((y * rect.Width + x) * 4, 4).ToArray());
                }
            }
        });
    }
}

/// <summary>
/// Prüft, ob RenderTargetBitmap in dieser Sitzung echte Pixel liefert (bei getrennter RDP-Sitzung nicht).
/// </summary>
public static class RenderCheck {
    public static bool IsRenderTargetBitmapWorking() {
        DrawingVisual visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen()) {
            dc.DrawRectangle(Brushes.Red, null, new Rect(0, 0, 2, 2));
        }
        RenderTargetBitmap target = new RenderTargetBitmap(2, 2, 96, 96, PixelFormats.Pbgra32);
        target.Render(visual);
        byte[] pixels = new byte[16];
        target.CopyPixels(pixels, 8, 0);
        return pixels[2] == 255 && pixels[3] == 255;
    }
}
