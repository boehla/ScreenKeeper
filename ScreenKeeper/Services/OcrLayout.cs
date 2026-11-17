using ScreenKeeper.Models;

namespace ScreenKeeper.Services;

/// <summary>
/// Reine Rechenhilfen der Texterkennung (ohne Windows-OCR, damit testbar): Skalierung des Ausschnitts, Rückrechnung der
/// Rechtecke in Bildpixel des Originals und Skalieren von Bgra32-Pixeln.
/// </summary>
public static class OcrLayout {
    /// <summary>Kleine Ausschnitte (bis zu dieser Pixelzahl) werden für kleine Schrift 2-fach vergrößert.</summary>
    public const long UpscalePixelLimit = 2_000_000;

    public const double UpscaleFactor = 2;

    /// <summary>
    /// Faktor, mit dem der Ausschnitt an die Erkennung geht: größer als <paramref name="maxDimension"/> → verkleinern, bis
    /// die längere Seite passt; kleine Ausschnitte (≤ <see cref="UpscalePixelLimit"/> Pixel) → 2-fach, sofern das passt;
    /// sonst 1.
    /// </summary>
    public static double GetScale(int width, int height, int maxDimension) {
        if(width <= 0 || height <= 0 || maxDimension <= 0) return 1;
        int longest = Math.Max(width, height);
        if(longest > maxDimension) return (double)maxDimension / longest;
        if((long)width * height <= UpscalePixelLimit && longest * UpscaleFactor <= maxDimension) return UpscaleFactor;
        return 1;
    }

    /// <summary>Größe des skalierten Ausschnitts (mindestens 1 Pixel, höchstens <paramref name="maxDimension"/>).</summary>
    public static (int Width, int Height) GetScaledSize(int width, int height, double scale, int maxDimension) {
        int scaledWidth = Math.Clamp((int)Math.Round(width * scale), 1, Math.Max(1, maxDimension));
        int scaledHeight = Math.Clamp((int)Math.Round(height * scale), 1, Math.Max(1, maxDimension));
        return (scaledWidth, scaledHeight);
    }

    /// <summary>
    /// Rechteck aus der Erkennung (Koordinaten im skalierten Ausschnitt) in Bildpixel des Originals: durch
    /// <paramref name="scale"/> teilen, um den Ursprung von <paramref name="region"/> verschieben, nach außen auf ganze
    /// Pixel runden und auf den Ausschnitt begrenzen.
    /// </summary>
    public static PixelRect ToImage(double x, double y, double width, double height, PixelRect region, double scale) {
        return ToImage(x, y, width, height, region, scale, scale);
    }

    /// <summary>Wie oben, mit eigenem Faktor je Achse (gerundete Größe des skalierten Ausschnitts).</summary>
    public static PixelRect ToImage(double x, double y, double width, double height, PixelRect region, double scaleX, double scaleY) {
        if(scaleX <= 0 || double.IsNaN(scaleX)) scaleX = 1;
        if(scaleY <= 0 || double.IsNaN(scaleY)) scaleY = 1;
        double left = region.X + x / scaleX;
        double top = region.Y + y / scaleY;
        double right = region.X + (x + Math.Max(0, width)) / scaleX;
        double bottom = region.Y + (y + Math.Max(0, height)) / scaleY;
        int l = Math.Clamp((int)Math.Floor(left + 1e-6), region.X, region.Right);
        int t = Math.Clamp((int)Math.Floor(top + 1e-6), region.Y, region.Bottom);
        int r = Math.Clamp((int)Math.Ceiling(right - 1e-6), region.X, region.Right);
        int b = Math.Clamp((int)Math.Ceiling(bottom - 1e-6), region.Y, region.Bottom);
        return new PixelRect(l, t, Math.Max(0, r - l), Math.Max(0, b - t));
    }

    /// <summary>Kleinstes Rechteck um alle (nicht leeren) Rechtecke; default, wenn es keins gibt.</summary>
    public static PixelRect Union(IEnumerable<PixelRect> rects) {
        bool any = false;
        int left = 0;
        int top = 0;
        int right = 0;
        int bottom = 0;
        foreach(PixelRect rect in rects) {
            if(rect.IsEmpty) continue;
            if(!any) {
                left = rect.X;
                top = rect.Y;
                right = rect.Right;
                bottom = rect.Bottom;
                any = true;
                continue;
            }
            left = Math.Min(left, rect.X);
            top = Math.Min(top, rect.Y);
            right = Math.Max(right, rect.Right);
            bottom = Math.Max(bottom, rect.Bottom);
        }
        return any ? new PixelRect(left, top, right - left, bottom - top) : default;
    }

    /// <summary>
    /// Bgra32-Pixel bilinear auf <paramref name="targetWidth"/> × <paramref name="targetHeight"/> skalieren (Pixelmitten
    /// aufeinander abgebildet). Gleiche Größe → dieselben Daten.
    /// </summary>
    public static byte[] ScaleBgra(byte[] pixels, int width, int height, int targetWidth, int targetHeight) {
        if(targetWidth == width && targetHeight == height) return pixels;
        byte[] result = new byte[targetWidth * targetHeight * 4];
        double scaleX = (double)width / targetWidth;
        double scaleY = (double)height / targetHeight;
        int stride = width * 4;
        for(int ty = 0; ty < targetHeight; ty++) {
            double sy = Math.Clamp((ty + 0.5) * scaleY - 0.5, 0, height - 1);
            int y0 = (int)sy;
            int y1 = Math.Min(y0 + 1, height - 1);
            double fy = sy - y0;
            for(int tx = 0; tx < targetWidth; tx++) {
                double sx = Math.Clamp((tx + 0.5) * scaleX - 0.5, 0, width - 1);
                int x0 = (int)sx;
                int x1 = Math.Min(x0 + 1, width - 1);
                double fx = sx - x0;
                int i00 = y0 * stride + x0 * 4;
                int i01 = y0 * stride + x1 * 4;
                int i10 = y1 * stride + x0 * 4;
                int i11 = y1 * stride + x1 * 4;
                int target = (ty * targetWidth + tx) * 4;
                for(int c = 0; c < 4; c++) {
                    double top = pixels[i00 + c] + (pixels[i01 + c] - pixels[i00 + c]) * fx;
                    double bottom = pixels[i10 + c] + (pixels[i11 + c] - pixels[i10 + c]) * fx;
                    result[target + c] = (byte)Math.Clamp((int)Math.Round(top + (bottom - top) * fy), 0, 255);
                }
            }
        }
        return result;
    }
}
