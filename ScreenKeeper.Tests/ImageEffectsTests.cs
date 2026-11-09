using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.Tests;

public class ImageEffectsTests {
    /// <summary>Schachbrett aus schwarzen und weißen Einzelpixeln (maximaler Kontrast), deckend.</summary>
    private static byte[] checkerboard(int width, int height) {
        byte[] pixels = new byte[width * height * 4];
        for(int y = 0; y < height; y++) {
            for(int x = 0; x < width; x++) {
                byte value = (x + y) % 2 == 0 ? (byte)0 : (byte)255;
                int i = (y * width + x) * 4;
                pixels[i] = value;
                pixels[i + 1] = value;
                pixels[i + 2] = value;
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    private static byte[] pattern(int width, int height) {
        byte[] pixels = new byte[width * height * 4];
        for(int y = 0; y < height; y++) {
            for(int x = 0; x < width; x++) {
                int i = (y * width + x) * 4;
                pixels[i] = (byte)(x * 7 + y);
                pixels[i + 1] = (byte)(y * 5 + 3);
                pixels[i + 2] = (byte)((x + y) * 11);
                pixels[i + 3] = 255;
            }
        }
        return pixels;
    }

    [Fact]
    public void Pixelate_EveryBlockIsUniformMeanOfOriginal() {
        const int width = 23;
        const int height = 17;
        const int block = 5;
        byte[] source = pattern(width, height);
        byte[] result = ImageEffects.Pixelate(source, width, height, block);

        for(int top = 0; top < height; top += block) {
            for(int left = 0; left < width; left += block) {
                int right = Math.Min(width, left + block);
                int bottom = Math.Min(height, top + block);
                for(int c = 0; c < 4; c++) {
                    long sum = 0;
                    int count = 0;
                    for(int y = top; y < bottom; y++) {
                        for(int x = left; x < right; x++) {
                            sum += source[(y * width + x) * 4 + c];
                            count++;
                        }
                    }
                    byte expected = (byte)((sum + count / 2) / count);
                    for(int y = top; y < bottom; y++) {
                        for(int x = left; x < right; x++) {
                            Assert.Equal(expected, result[(y * width + x) * 4 + c]);
                        }
                    }
                }
            }
        }
    }

    [Fact]
    public void Pixelate_HighContrastPattern_KeepsNoOriginalPixels() {
        const int width = 40;
        const int height = 40;
        byte[] source = checkerboard(width, height);
        foreach(int block in new[] { 2, 4, 10 }) {
            byte[] result = ImageEffects.Pixelate(source, width, height, block);
            for(int i = 0; i < width * height; i++) {
                byte b = result[i * 4];
                // Schwarz/Weiß wird zu Grau – kein einziger Originalwert bleibt stehen
                Assert.NotEqual(0, b);
                Assert.NotEqual(255, b);
                Assert.InRange(b, 126, 129);
            }
        }
    }

    [Fact]
    public void Pixelate_BlockSizeBelowMinimum_IsRaised() {
        byte[] source = checkerboard(8, 8);
        byte[] result = ImageEffects.Pixelate(source, 8, 8, 1);
        Assert.NotEqual(source, result);
        Assert.Equal(ImageEffects.Pixelate(source, 8, 8, ImageEffects.MinBlockSize), result);
    }

    [Fact]
    public void Blur_SoftensEdge_AndKeepsMean() {
        const int width = 60;
        const int height = 20;
        byte[] source = new byte[width * height * 4];
        for(int y = 0; y < height; y++) {
            for(int x = 0; x < width; x++) {
                int i = (y * width + x) * 4;
                byte value = x < width / 2 ? (byte)0 : (byte)255;
                source[i] = value;
                source[i + 1] = value;
                source[i + 2] = value;
                source[i + 3] = 255;
            }
        }
        byte[] result = ImageEffects.Blur(source, width, height, 8);

        // an der Kante entstehen Zwischenwerte, weit weg bleibt es schwarz bzw. weiß
        int row = 10 * width * 4;
        Assert.InRange(result[row + (width / 2 - 1) * 4], 60, 200);
        Assert.InRange(result[row + (width / 2) * 4], 60, 200);
        Assert.True(result[row + (width / 2 - 3) * 4] > 0);
        Assert.True(result[row + (width / 2 + 2) * 4] < 255);
        Assert.Equal(0, result[row]);
        Assert.Equal(255, result[row + (width - 1) * 4]);
        // monoton steigend über die Kante
        for(int x = 1; x < width; x++) Assert.True(result[row + x * 4] >= result[row + (x - 1) * 4]);

        double meanBefore = source.Where((v, i) => i % 4 == 0).Average(v => (double)v);
        double meanAfter = result.Where((v, i) => i % 4 == 0).Average(v => (double)v);
        Assert.InRange(meanAfter - meanBefore, -1.5, 1.5);
        // Alphakanal bleibt deckend
        Assert.All(result.Where((v, i) => i % 4 == 3), a => Assert.Equal(255, a));
    }

    [Fact]
    public void Blur_UniformImage_IsUnchanged() {
        byte[] source = new byte[30 * 10 * 4];
        for(int i = 0; i < source.Length; i += 4) {
            source[i] = 10;
            source[i + 1] = 20;
            source[i + 2] = 30;
            source[i + 3] = 255;
        }
        Assert.Equal(source, ImageEffects.Blur(source, 30, 10, 12));
    }

    [Fact]
    public void BlurRadii_AreThreePositivePasses() {
        foreach(int radius in new[] { 1, 2, 8, 24 }) {
            int[] radii = ImageEffects.GetBoxRadii(radius);
            Assert.Equal(ImageEffects.BlurPasses, radii.Length);
            Assert.All(radii, r => Assert.True(r >= 1));
            Assert.Equal(radii.Sum(), ImageEffects.GetBlurMargin(radius));
        }
        Assert.True(ImageEffects.GetBlurMargin(24) > ImageEffects.GetBlurMargin(4));
    }

    [Fact]
    public void Crop_CopiesRegion() {
        byte[] source = pattern(10, 8);
        byte[] crop = ImageEffects.Crop(source, 10, 8, 3, 2, 4, 5);
        Assert.Equal(4 * 5 * 4, crop.Length);
        Assert.Equal(source.AsSpan((2 * 10 + 3) * 4, 4).ToArray(), crop.AsSpan(0, 4).ToArray());
        Assert.Equal(source.AsSpan((6 * 10 + 6) * 4, 4).ToArray(), crop.AsSpan((4 * 4 + 3) * 4, 4).ToArray());
        Assert.Throws<ArgumentOutOfRangeException>(() => ImageEffects.Crop(source, 10, 8, 8, 0, 4, 1));
    }

    [Fact]
    public void ComputeEffect_PixelatedRegionContainsNoOriginalPixels() {
        // CPU-Pfad des Renderers (ohne RenderTargetBitmap): Original-Schachbrett → keine Schwarz-/Weißpixel im Ergebnis
        Sta.Run(() => {
            const int size = 64;
            byte[] pixels = checkerboard(size, size);
            BitmapSource original = BitmapSource.Create(size, size, 96, 96, System.Windows.Media.PixelFormats.Bgra32, null, pixels, size * 4);
            original.Freeze();
            PixelRect rect = new PixelRect(8, 8, 40, 30);
            BitmapSource effect = RenderService.ComputeEffect(original, rect, false, 10);
            Assert.Equal(rect.Width, effect.PixelWidth);
            Assert.Equal(rect.Height, effect.PixelHeight);
            byte[] result = TestImages.GetPixels(effect);
            for(int i = 0; i < result.Length; i += 4) {
                Assert.NotEqual(0, result[i]);
                Assert.NotEqual(255, result[i]);
            }

            BitmapSource blurred = RenderService.ComputeEffect(original, rect, true, 8);
            byte[] blur = TestImages.GetPixels(blurred);
            Assert.All(blur.Where((v, i) => i % 4 == 0), v => Assert.InRange(v, 100, 155));
        });
    }
}
