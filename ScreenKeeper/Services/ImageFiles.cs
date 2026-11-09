using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ScreenKeeper.Services;

/// <summary>
/// Hilfsfunktionen zum Kodieren, Dekodieren und atomaren Schreiben von Bildern.
/// Alle Methoden arbeiten mit eingefrorenen Bitmaps und sind damit auch aus Hintergrund-Tasks nutzbar.
/// </summary>
public static class ImageFiles {
    public static byte[] EncodePng(BitmapSource image) {
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using (MemoryStream stream = new MemoryStream()) {
            encoder.Save(stream);
            return stream.ToArray();
        }
    }

    public static void WritePng(BitmapSource image, string path) {
        PngBitmapEncoder encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        AtomicFile.Write(path, stream => encoder.Save(stream));
    }

    public static void WriteJpeg(BitmapSource image, string path, int quality) {
        JpegBitmapEncoder encoder = new JpegBitmapEncoder { QualityLevel = quality };
        BitmapSource opaque = image.Format == PixelFormats.Bgr24 ? image : new FormatConvertedBitmap(image, PixelFormats.Bgr24, null, 0);
        encoder.Frames.Add(BitmapFrame.Create(opaque));
        AtomicFile.Write(path, stream => encoder.Save(stream));
    }

    /// <summary>
    /// Lädt ein Bild vollständig in den Speicher und liefert es als Bgra32 mit 96 dpi (1 Bildpixel = 1 Einheit).
    /// </summary>
    public static BitmapSource LoadBgra32(string path) {
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            return decodeBgra32(stream);
        }
    }

    public static BitmapSource DecodeBgra32(byte[] data) {
        using (MemoryStream stream = new MemoryStream(data)) {
            return decodeBgra32(stream);
        }
    }

    /// <summary>
    /// Kopiert ein Bild als Bgra32 mit 96 dpi (eingefroren).
    /// </summary>
    public static BitmapSource ToBgra32(BitmapSource source) {
        BitmapSource converted = source.Format == PixelFormats.Bgra32 ? source : new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        byte[] pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        BitmapSource result = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        result.Freeze();
        return result;
    }

    /// <summary>
    /// Bildgröße in Pixeln, ohne das Bild zu dekodieren.
    /// </summary>
    public static (int Width, int Height) ReadSize(string path) {
        using (FileStream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read)) {
            BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            BitmapFrame frame = decoder.Frames[0];
            return (frame.PixelWidth, frame.PixelHeight);
        }
    }

    /// <summary>
    /// Lädt ein Bild verkleinert auf höchstens <paramref name="maxWidth"/> Pixel Breite (beim Dekodieren skaliert,
    /// eingefroren, ohne Dateisperre). Auch aus Hintergrund-Threads nutzbar.
    /// </summary>
    public static BitmapSource LoadScaled(string path, int maxWidth) {
        byte[] data = File.ReadAllBytes(path);
        int width;
        using (MemoryStream probe = new MemoryStream(data, false)) {
            BitmapDecoder decoder = BitmapDecoder.Create(probe, BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.None);
            width = decoder.Frames[0].PixelWidth;
        }
        using (MemoryStream stream = new MemoryStream(data, false)) {
            BitmapImage image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            if(width > maxWidth) image.DecodePixelWidth = maxWidth;
            image.StreamSource = stream;
            image.EndInit();
            image.Freeze();
            return image;
        }
    }

    private static BitmapSource decodeBgra32(Stream stream) {
        BitmapDecoder decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
        return ToBgra32(decoder.Frames[0]);
    }
}
