using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

namespace ScreenKeeper.Services;

public interface IClipboardService {
    /// <summary>Legt das Bild als Bitmap (CF_BITMAP/DIB) und als PNG in die Zwischenablage. Muss im UI-Thread laufen.</summary>
    void SetImage(BitmapSource image);
}

/// <summary>
/// Zwischenablage mit Wiederholversuchen (sie kann kurzzeitig von einem anderen Programm belegt sein).
/// </summary>
public sealed class ClipboardService : IClipboardService {
    private const int attempts = 10;
    private const int retryDelayMs = 100;

    public void SetImage(BitmapSource image) {
        using (MemoryStream png = new MemoryStream(ImageFiles.EncodePng(image))) {
            DataObject data = new DataObject();
            data.SetImage(image);
            data.SetData("PNG", png, false);
            for(int attempt = 1; ; attempt++) {
                try {
                    // copy = true: Daten bleiben nach Programmende erhalten (OleFlushClipboard)
                    Clipboard.SetDataObject(data, true);
                    return;
                } catch(ExternalException ex) {
                    if(attempt >= attempts) throw;
                    Log.Warn($"Zwischenablage belegt (Versuch {attempt}): {ex.Message}");
                    Thread.Sleep(retryDelayMs);
                }
            }
        }
    }
}
