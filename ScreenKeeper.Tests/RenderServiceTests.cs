using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.Tests;

public class RenderServiceTests {
    [Fact]
    public void Render_CutsRegionExactly() {
        Sta.Run(() => {
            const int width = 64;
            const int height = 48;
            BitmapSource original = TestImages.CreatePattern(width, height);
            CaptureDocument doc = new CaptureDocument { VirtualScreen = new PixelRect(0, 0, width, height) };
            PixelRect region = new PixelRect(13, 7, 21, 17);

            BitmapSource rendered = new RenderService().Render(doc, original, region);

            Assert.Equal(region.Width, rendered.PixelWidth);
            Assert.Equal(region.Height, rendered.PixelHeight);
            byte[] source = TestImages.GetPixels(original);
            byte[] result = TestImages.GetPixels(rendered);
            for(int y = 0; y < region.Height; y++) {
                for(int x = 0; x < region.Width; x++) {
                    int s = ((region.Y + y) * width + region.X + x) * 4;
                    int r = (y * region.Width + x) * 4;
                    for(int c = 0; c < 4; c++) {
                        Assert.True(source[s + c] == result[r + c], $"Pixel ({x},{y}) Kanal {c}: erwartet {source[s + c]}, erhalten {result[r + c]}");
                    }
                }
            }
        });
    }

    [Fact]
    public void Render_FullImageIsIdentical() {
        Sta.Run(() => {
            BitmapSource original = TestImages.CreatePattern(40, 30);
            CaptureDocument doc = new CaptureDocument();
            BitmapSource rendered = new RenderService().Render(doc, original, new PixelRect(0, 0, 40, 30));
            Assert.Equal(TestImages.GetPixels(original), TestImages.GetPixels(rendered));
        });
    }

    [Fact]
    public void Render_ClipsRegionToImage() {
        Sta.Run(() => {
            BitmapSource original = TestImages.CreatePattern(40, 30);
            BitmapSource rendered = new RenderService().Render(new CaptureDocument(), original, new PixelRect(30, 20, 50, 50));
            Assert.Equal(10, rendered.PixelWidth);
            Assert.Equal(10, rendered.PixelHeight);
            Assert.Throws<ArgumentException>(() => new RenderService().Render(new CaptureDocument(), original, new PixelRect(100, 100, 5, 5)));
        });
    }

    [Fact]
    public void Render_DrawsCapturedCursorAtHotspot() {
        Sta.Run(() => {
            // 4×4 roter, deckender Zeiger mit Hotspot (1,1)
            byte[] cursorPixels = new byte[4 * 4 * 4];
            for(int i = 0; i < 16; i++) {
                cursorPixels[i * 4 + 2] = 255;
                cursorPixels[i * 4 + 3] = 255;
            }
            BitmapSource cursorBitmap = BitmapSource.Create(4, 4, 96, 96, PixelFormats.Bgra32, null, cursorPixels, 16);
            string png = Convert.ToBase64String(ImageFiles.EncodePng(cursorBitmap));

            BitmapSource original = TestImages.CreatePattern(20, 20);
            CaptureDocument doc = new CaptureDocument {
                Cursor = new CursorInfo { Png = png, X = 10, Y = 10, HotspotX = 1, HotspotY = 1, CapturedVisible = true },
                Annotations = new List<Annotation> { new CursorAnnotation { X = 10, Y = 10, Kind = CursorKinds.Captured } }
            };
            byte[] result = TestImages.GetPixels(new RenderService().Render(doc, original, new PixelRect(0, 0, 20, 20)));
            byte[] source = TestImages.GetPixels(original);

            // Zeiger deckt (9..12, 9..12) ab
            int inside = (9 * 20 + 9) * 4;
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, result.AsSpan(inside, 4).ToArray());
            int corner = (12 * 20 + 12) * 4;
            Assert.Equal(new byte[] { 0, 0, 255, 255 }, result.AsSpan(corner, 4).ToArray());
            int outside = (8 * 20 + 8) * 4;
            Assert.Equal(source.AsSpan(outside, 4).ToArray(), result.AsSpan(outside, 4).ToArray());

            // Ausgeblendet → Original
            ((CursorAnnotation)doc.Annotations[0]).Visible = false;
            byte[] hidden = TestImages.GetPixels(new RenderService().Render(doc, original, new PixelRect(0, 0, 20, 20)));
            Assert.Equal(source, hidden);
        });
    }

    [Fact]
    public void SystemCursors_CanBeLoaded() {
        Sta.Run(() => {
            foreach(string kind in new[] { CursorKinds.Arrow, CursorKinds.Hand, CursorKinds.IBeam }) {
                CursorImage? image = RenderService.GetSystemCursor(kind);
                Assert.NotNull(image);
                Assert.True(image!.Bitmap.PixelWidth > 0);
                byte[] pixels = TestImages.GetPixels(image.Bitmap);
                bool anyVisible = false;
                for(int i = 3; i < pixels.Length; i += 4) {
                    if(pixels[i] != 0) {
                        anyVisible = true;
                        break;
                    }
                }
                Assert.True(anyVisible, $"Zeiger {kind} ist komplett transparent");
            }
            Assert.Null(RenderService.GetSystemCursor("gibtsnicht"));
        });
    }
}
