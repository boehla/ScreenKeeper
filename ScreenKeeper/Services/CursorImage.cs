using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenKeeper.Services.Native;

namespace ScreenKeeper.Services;

/// <summary>
/// Bild eines Mauszeigers mit Transparenz und Hotspot.
/// </summary>
public sealed class CursorImage {
    public CursorImage(BitmapSource bitmap, int hotspotX, int hotspotY) {
        Bitmap = bitmap;
        HotspotX = hotspotX;
        HotspotY = hotspotY;
    }

    /// <summary>Zeigerbild (Bgra32, 96 dpi, eingefroren).</summary>
    public BitmapSource Bitmap { get; }

    public int HotspotX { get; }

    public int HotspotY { get; }

    public byte[] ToPng() {
        return ImageFiles.EncodePng(Bitmap);
    }

    /// <summary>
    /// Wandelt ein Cursor-Handle (HCURSOR) in ein Bild um. Behandelt Zeiger mit Alphakanal, farbige Zeiger mit
    /// Maske und monochrome Zeiger (z. B. den klassischen Textcursor). Invertierende Pixel lassen sich nicht als
    /// Bild darstellen; sie werden schwarz mit weißem Rand gezeichnet, damit sie auf hellem und dunklem Grund sichtbar sind.
    /// Das Handle wird nicht freigegeben (Cursor-Handles sind geteilte Ressourcen).
    /// </summary>
    public static CursorImage? FromHandle(IntPtr cursorHandle) {
        if(cursorHandle == IntPtr.Zero) return null;
        if(!User32.GetIconInfo(cursorHandle, out ICONINFO info)) return null;
        IntPtr screenDc = User32.GetDC(IntPtr.Zero);
        try {
            if(info.hbmMask == IntPtr.Zero) return null;
            if(Gdi32.GetObject(info.hbmMask, System.Runtime.InteropServices.Marshal.SizeOf<BITMAP>(), out BITMAP maskInfo) == 0) return null;
            int width = maskInfo.bmWidth;
            bool monochrome = info.hbmColor == IntPtr.Zero;
            int height = monochrome ? maskInfo.bmHeight / 2 : maskInfo.bmHeight;
            if(width <= 0 || height <= 0) return null;

            bool[] mask = readMonoBits(screenDc, info.hbmMask, width, maskInfo.bmHeight);
            byte[] pixels = new byte[width * height * 4];
            bool[] invert = new bool[width * height];

            if(monochrome) {
                // Obere Hälfte: AND-Maske, untere Hälfte: XOR-Maske
                for(int y = 0; y < height; y++) {
                    for(int x = 0; x < width; x++) {
                        int i = y * width + x;
                        bool andBit = mask[i];
                        bool xorBit = mask[(y + height) * width + x];
                        if(!andBit) {
                            byte value = xorBit ? (byte)255 : (byte)0;
                            setPixel(pixels, i, value, value, value, 255);
                        } else if(xorBit) {
                            invert[i] = true;
                        }
                    }
                }
            } else {
                byte[] color = readColorBits(screenDc, info.hbmColor, width, height);
                bool hasAlpha = false;
                for(int i = 3; i < color.Length; i += 4) {
                    if(color[i] != 0) {
                        hasAlpha = true;
                        break;
                    }
                }
                for(int i = 0; i < width * height; i++) {
                    byte b = color[i * 4];
                    byte g = color[i * 4 + 1];
                    byte r = color[i * 4 + 2];
                    if(hasAlpha) {
                        setPixel(pixels, i, b, g, r, color[i * 4 + 3]);
                    } else if(!mask[i]) {
                        setPixel(pixels, i, b, g, r, 255);
                    } else if(b != 0 || g != 0 || r != 0) {
                        invert[i] = true;
                    }
                }
            }

            applyInvertPixels(pixels, invert, width, height);
            BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
            bitmap.Freeze();
            return new CursorImage(bitmap, info.xHotspot, info.yHotspot);
        } finally {
            User32.ReleaseDC(IntPtr.Zero, screenDc);
            if(info.hbmMask != IntPtr.Zero) Gdi32.DeleteObject(info.hbmMask);
            if(info.hbmColor != IntPtr.Zero) Gdi32.DeleteObject(info.hbmColor);
        }
    }

    /// <summary>
    /// Lädt einen Windows-Standardzeiger (IDC_ARROW, IDC_HAND, IDC_IBEAM …).
    /// </summary>
    public static CursorImage? FromSystemCursor(int cursorId) {
        IntPtr handle = User32.LoadCursor(IntPtr.Zero, new IntPtr(cursorId));
        return FromHandle(handle);
    }

    private static void setPixel(byte[] pixels, int index, byte b, byte g, byte r, byte a) {
        pixels[index * 4] = b;
        pixels[index * 4 + 1] = g;
        pixels[index * 4 + 2] = r;
        pixels[index * 4 + 3] = a;
    }

    /// <summary>
    /// Invertierende Pixel schwarz zeichnen und transparente Nachbarn weiß umranden.
    /// </summary>
    private static void applyInvertPixels(byte[] pixels, bool[] invert, int width, int height) {
        bool any = false;
        for(int i = 0; i < invert.Length; i++) {
            if(invert[i]) {
                setPixel(pixels, i, 0, 0, 0, 255);
                any = true;
            }
        }
        if(!any) return;
        for(int y = 0; y < height; y++) {
            for(int x = 0; x < width; x++) {
                int i = y * width + x;
                if(invert[i] || pixels[i * 4 + 3] != 0) continue;
                bool nearInvert = false;
                for(int dy = -1; dy <= 1 && !nearInvert; dy++) {
                    for(int dx = -1; dx <= 1; dx++) {
                        int nx = x + dx;
                        int ny = y + dy;
                        if(nx < 0 || ny < 0 || nx >= width || ny >= height) continue;
                        if(invert[ny * width + nx]) {
                            nearInvert = true;
                            break;
                        }
                    }
                }
                if(nearInvert) setPixel(pixels, i, 255, 255, 255, 255);
            }
        }
    }

    private static bool[] readMonoBits(IntPtr dc, IntPtr bitmap, int width, int height) {
        int stride = ((width + 31) / 32) * 4;
        byte[] buffer = new byte[stride * height];
        MONOBITMAPINFO bmi = new MONOBITMAPINFO { bmiHeader = BITMAPINFOHEADER.TopDown(width, height, 1) };
        Gdi32.GetDIBits(dc, bitmap, 0, (uint)height, buffer, ref bmi, Gdi32.DIB_RGB_COLORS);
        bool[] bits = new bool[width * height];
        for(int y = 0; y < height; y++) {
            for(int x = 0; x < width; x++) {
                byte value = buffer[y * stride + x / 8];
                bits[y * width + x] = (value & (0x80 >> (x % 8))) != 0;
            }
        }
        return bits;
    }

    private static byte[] readColorBits(IntPtr dc, IntPtr bitmap, int width, int height) {
        byte[] buffer = new byte[width * height * 4];
        BITMAPINFOHEADER bmi = BITMAPINFOHEADER.TopDown(width, height, 32);
        Gdi32.GetDIBits(dc, bitmap, 0, (uint)height, buffer, ref bmi, Gdi32.DIB_RGB_COLORS);
        return buffer;
    }
}
