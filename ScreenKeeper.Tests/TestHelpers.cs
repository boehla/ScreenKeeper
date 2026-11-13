using System.Runtime.ExceptionServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenKeeper.Services;

namespace ScreenKeeper.Tests;

/// <summary>
/// Führt WPF-Code in einem eigenen STA-Thread aus (RenderTargetBitmap, Visuals).
/// </summary>
public static class Sta {
    public static void Run(Action action) {
        ExceptionDispatchInfo? error = null;
        Thread thread = new Thread(() => {
            try {
                action();
            } catch(Exception ex) {
                error = ExceptionDispatchInfo.Capture(ex);
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        error?.Throw();
    }
}

/// <summary>
/// Temporärer Ordner, der nach dem Test gelöscht wird.
/// </summary>
public sealed class TempFolder : IDisposable {
    public TempFolder() {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ScreenKeeperTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
        Log.FileOverride = System.IO.Path.Combine(Path, "log.txt");
    }

    public string Path { get; }

    public void Dispose() {
        try {
            Directory.Delete(Path, true);
        } catch(IOException ex) {
            // Aufräumen ist nicht testrelevant (z. B. Datei noch kurz gesperrt)
            Console.WriteLine($"Temp-Ordner nicht gelöscht: {ex.Message}");
        }
    }
}

/// <summary>
/// Admin-Autostart ohne UAC und Aufgabenplanung: liefert <see cref="Result"/> und zählt die Aufrufe.
/// </summary>
public sealed class FakeElevatedAutostart : IElevatedAutostartService {
    public bool IsEnabled { get; set; }

    public bool CanElevate { get; set; } = true;

    public ElevatedAutostartResult Result { get; set; } = ElevatedAutostartResult.Success;

    public List<bool> Calls { get; } = new List<bool>();

    public ElevatedAutostartResult SetEnabled(bool enabled, out string error) {
        Calls.Add(enabled);
        error = Result == ElevatedAutostartResult.Failed ? "kaputt" : "";
        if(Result == ElevatedAutostartResult.Success) IsEnabled = enabled;
        return Result;
    }
}

public static class TestImages {
    /// <summary>
    /// Bgra32-Bild, in dem jedes Pixel eindeutig aus seiner Position berechnet ist (deckend).
    /// </summary>
    public static BitmapSource CreatePattern(int width, int height) {
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
        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        bitmap.Freeze();
        return bitmap;
    }

    public static byte[] GetPixels(BitmapSource bitmap) {
        BitmapSource converted = bitmap.Format == PixelFormats.Bgra32 || bitmap.Format == PixelFormats.Pbgra32 ? bitmap : new FormatConvertedBitmap(bitmap, PixelFormats.Bgra32, null, 0);
        int stride = converted.PixelWidth * 4;
        byte[] pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);
        return pixels;
    }
}
