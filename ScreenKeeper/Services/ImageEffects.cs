namespace ScreenKeeper.Services;

/// <summary>
/// Bildeffekte für Verpixeln und Weichzeichnen auf rohen Bgra32-Pixeln (4 Bytes je Pixel, Zeilen ohne Auffüllung).
/// UI-frei, damit es ohne WPF-Darstellung testbar ist. Die Ergebnisse werden immer aus dem Original berechnet.
/// </summary>
public static class ImageEffects {
    /// <summary>Kleinste Blockgröße; bei 1 wäre das Ergebnis das Original.</summary>
    public const int MinBlockSize = 2;

    /// <summary>Anzahl der Box-Blur-Durchgänge (drei ergeben fast einen Gauß-Weichzeichner).</summary>
    public const int BlurPasses = 3;

    /// <summary>
    /// Verpixeln: Blöcke ab der linken oberen Ecke des Puffers; jeder Block bekommt den gerundeten Mittelwert
    /// seiner Originalpixel (je Kanal). Randblöcke sind entsprechend kleiner. Liefert einen neuen Puffer.
    /// </summary>
    public static byte[] Pixelate(byte[] pixels, int width, int height, int blockSize) {
        validate(pixels, width, height);
        int block = Math.Max(MinBlockSize, blockSize);
        byte[] result = new byte[pixels.Length];
        int stride = width * 4;
        for(int top = 0; top < height; top += block) {
            int bottom = Math.Min(height, top + block);
            for(int left = 0; left < width; left += block) {
                int right = Math.Min(width, left + block);
                long sumB = 0;
                long sumG = 0;
                long sumR = 0;
                long sumA = 0;
                for(int y = top; y < bottom; y++) {
                    int i = y * stride + left * 4;
                    for(int x = left; x < right; x++) {
                        sumB += pixels[i];
                        sumG += pixels[i + 1];
                        sumR += pixels[i + 2];
                        sumA += pixels[i + 3];
                        i += 4;
                    }
                }
                long count = (long)(right - left) * (bottom - top);
                byte b = (byte)((sumB + count / 2) / count);
                byte g = (byte)((sumG + count / 2) / count);
                byte r = (byte)((sumR + count / 2) / count);
                byte a = (byte)((sumA + count / 2) / count);
                for(int y = top; y < bottom; y++) {
                    int i = y * stride + left * 4;
                    for(int x = left; x < right; x++) {
                        result[i] = b;
                        result[i + 1] = g;
                        result[i + 2] = r;
                        result[i + 3] = a;
                        i += 4;
                    }
                }
            }
        }
        return result;
    }

    /// <summary>
    /// Weichzeichnen: dreifacher Box-Blur (≈ Gauß mit σ = <paramref name="radius"/> / 2), Ränder werden fortgesetzt.
    /// Liefert einen neuen Puffer gleicher Größe.
    /// </summary>
    public static byte[] Blur(byte[] pixels, int width, int height, int radius) {
        validate(pixels, width, height);
        byte[] current = (byte[])pixels.Clone();
        if(radius <= 0) return current;
        byte[] temp = new byte[pixels.Length];
        foreach(int boxRadius in GetBoxRadii(radius)) {
            if(boxRadius <= 0) continue;
            blurHorizontal(current, temp, width, height, boxRadius);
            blurVertical(temp, current, width, height, boxRadius);
        }
        return current;
    }

    /// <summary>
    /// Radien der Box-Durchgänge für einen Gauß-Weichzeichner mit σ = <paramref name="radius"/> / 2
    /// (Verfahren „boxes for Gauss“). Jeder Durchgang hat mindestens Radius 1.
    /// </summary>
    public static int[] GetBoxRadii(int radius) {
        int[] radii = new int[BlurPasses];
        if(radius <= 0) return radii;
        double sigma = Math.Max(0.5, radius / 2.0);
        int n = BlurPasses;
        double idealWidth = Math.Sqrt(12 * sigma * sigma / n + 1);
        int lower = (int)Math.Floor(idealWidth);
        if(lower % 2 == 0) lower--;
        int upper = lower + 2;
        double idealCount = (12 * sigma * sigma - n * lower * lower - 4 * n * lower - 3 * n) / (-4.0 * lower - 4);
        int count = (int)Math.Round(idealCount);
        for(int i = 0; i < n; i++) {
            int size = i < count ? lower : upper;
            radii[i] = Math.Max(1, (size - 1) / 2);
        }
        return radii;
    }

    /// <summary>
    /// Wie weit der Weichzeichner Pixel außerhalb eines Rechtecks berücksichtigt (Summe der Box-Radien).
    /// </summary>
    public static int GetBlurMargin(int radius) {
        return GetBoxRadii(radius).Sum();
    }

    /// <summary>
    /// Ausschnitt (<paramref name="x"/>, <paramref name="y"/>, <paramref name="cropWidth"/> × <paramref name="cropHeight"/>) kopieren.
    /// </summary>
    public static byte[] Crop(byte[] pixels, int width, int height, int x, int y, int cropWidth, int cropHeight) {
        validate(pixels, width, height);
        if(x < 0 || y < 0 || cropWidth < 0 || cropHeight < 0 || x + cropWidth > width || y + cropHeight > height) {
            throw new ArgumentOutOfRangeException(nameof(x), "Der Ausschnitt liegt nicht vollständig im Bild.");
        }
        byte[] result = new byte[cropWidth * cropHeight * 4];
        for(int row = 0; row < cropHeight; row++) {
            Buffer.BlockCopy(pixels, ((y + row) * width + x) * 4, result, row * cropWidth * 4, cropWidth * 4);
        }
        return result;
    }

    private static void blurHorizontal(byte[] source, byte[] target, int width, int height, int radius) {
        int window = 2 * radius + 1;
        int half = window / 2;
        int stride = width * 4;
        int last = width - 1;
        for(int y = 0; y < height; y++) {
            int row = y * stride;
            int sumB = 0;
            int sumG = 0;
            int sumR = 0;
            int sumA = 0;
            for(int k = -radius; k <= radius; k++) {
                int i = row + Math.Clamp(k, 0, last) * 4;
                sumB += source[i];
                sumG += source[i + 1];
                sumR += source[i + 2];
                sumA += source[i + 3];
            }
            for(int x = 0; x < width; x++) {
                int o = row + x * 4;
                target[o] = (byte)((sumB + half) / window);
                target[o + 1] = (byte)((sumG + half) / window);
                target[o + 2] = (byte)((sumR + half) / window);
                target[o + 3] = (byte)((sumA + half) / window);
                int add = row + Math.Min(x + radius + 1, last) * 4;
                int remove = row + Math.Max(x - radius, 0) * 4;
                sumB += source[add] - source[remove];
                sumG += source[add + 1] - source[remove + 1];
                sumR += source[add + 2] - source[remove + 2];
                sumA += source[add + 3] - source[remove + 3];
            }
        }
    }

    private static void blurVertical(byte[] source, byte[] target, int width, int height, int radius) {
        int window = 2 * radius + 1;
        int half = window / 2;
        int stride = width * 4;
        int last = height - 1;
        for(int x = 0; x < width; x++) {
            int column = x * 4;
            int sumB = 0;
            int sumG = 0;
            int sumR = 0;
            int sumA = 0;
            for(int k = -radius; k <= radius; k++) {
                int i = Math.Clamp(k, 0, last) * stride + column;
                sumB += source[i];
                sumG += source[i + 1];
                sumR += source[i + 2];
                sumA += source[i + 3];
            }
            for(int y = 0; y < height; y++) {
                int o = y * stride + column;
                target[o] = (byte)((sumB + half) / window);
                target[o + 1] = (byte)((sumG + half) / window);
                target[o + 2] = (byte)((sumR + half) / window);
                target[o + 3] = (byte)((sumA + half) / window);
                int add = Math.Min(y + radius + 1, last) * stride + column;
                int remove = Math.Max(y - radius, 0) * stride + column;
                sumB += source[add] - source[remove];
                sumG += source[add + 1] - source[remove + 1];
                sumR += source[add + 2] - source[remove + 2];
                sumA += source[add + 3] - source[remove + 3];
            }
        }
    }

    private static void validate(byte[] pixels, int width, int height) {
        if(width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Breite und Höhe müssen positiv sein.");
        if(pixels.Length != width * height * 4) throw new ArgumentException($"Der Puffer hat {pixels.Length} Bytes, erwartet {width * height * 4}.", nameof(pixels));
    }
}
