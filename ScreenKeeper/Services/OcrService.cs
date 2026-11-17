using System.Diagnostics;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ScreenKeeper.Models;
using WinOcrEngine = global::Windows.Media.Ocr.OcrEngine;
using WinOcrLine = global::Windows.Media.Ocr.OcrLine;
using WinOcrResult = global::Windows.Media.Ocr.OcrResult;
using WinOcrWord = global::Windows.Media.Ocr.OcrWord;
using WinBitmapAlphaMode = global::Windows.Graphics.Imaging.BitmapAlphaMode;
using WinBitmapPixelFormat = global::Windows.Graphics.Imaging.BitmapPixelFormat;
using WinSoftwareBitmap = global::Windows.Graphics.Imaging.SoftwareBitmap;

namespace ScreenKeeper.Services;

public interface IOcrService {
    /// <summary>
    /// Text im Ausschnitt <paramref name="region"/> (Bildpixel) des eingefrorenen Originals erkennen. Läuft im
    /// Hintergrund. Null, wenn die Erkennung nicht verfügbar ist, fehlschlägt oder abgebrochen wird – nie eine Ausnahme
    /// (Fehler stehen nur im Protokoll).
    /// </summary>
    Task<RecognizedText?> RecognizeAsync(BitmapSource original, PixelRect region, CancellationToken cancellation);
}

/// <summary>
/// Texterkennung über <c>Windows.Media.Ocr</c> (offline, Sprachen des Benutzerprofils). Der Ausschnitt geht als reine
/// Bildpixel (ohne Zeichnungen) an die Erkennung, bei kleinen Ausschnitten 2-fach vergrößert, bei zu großen verkleinert
/// (<see cref="OcrLayout.GetScale"/>); die Rechtecke werden in Bildpixel des Originals zurückgerechnet. Aufrufe laufen
/// nacheinander.
/// </summary>
public sealed class OcrService : IOcrService {
    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
    private readonly object engineSync = new object();
    private WinOcrEngine? engine;
    private bool engineChecked;

    public async Task<RecognizedText?> RecognizeAsync(BitmapSource original, PixelRect region, CancellationToken cancellation) {
        try {
            await gate.WaitAsync(cancellation).ConfigureAwait(false);
        } catch(OperationCanceledException) {
            return null;
        }
        try {
            return await Task.Run(() => recognizeAsync(original, region, cancellation), cancellation).ConfigureAwait(false);
        } catch(OperationCanceledException) {
            return null;
        } catch(Exception ex) {
            Log.Warn($"Texterkennung fehlgeschlagen ({region.Width}×{region.Height} bei {region.X},{region.Y}): {ex.GetType().Name}: {ex.Message}");
            return null;
        } finally {
            gate.Release();
        }
    }

    private async Task<RecognizedText?> recognizeAsync(BitmapSource original, PixelRect region, CancellationToken cancellation) {
        WinOcrEngine? ocr = getEngine();
        if(ocr == null) return null;
        PixelRect clipped = region.Intersect(new PixelRect(0, 0, original.PixelWidth, original.PixelHeight));
        if(clipped.IsEmpty) return null;
        Stopwatch watch = Stopwatch.StartNew();
        int maxDimension = (int)WinOcrEngine.MaxImageDimension;
        double scale = OcrLayout.GetScale(clipped.Width, clipped.Height, maxDimension);
        (int width, int height) = OcrLayout.GetScaledSize(clipped.Width, clipped.Height, scale, maxDimension);
        // tatsächlicher Faktor je Achse (durch das Runden der Größe minimal verschieden)
        double scaleX = (double)width / clipped.Width;
        double scaleY = (double)height / clipped.Height;
        byte[] pixels = OcrLayout.ScaleBgra(copyPixels(original, clipped), clipped.Width, clipped.Height, width, height);
        cancellation.ThrowIfCancellationRequested();

        WinOcrResult result;
        using (WinSoftwareBitmap bitmap = WinSoftwareBitmap.CreateCopyFromBuffer(pixels.AsBuffer(), WinBitmapPixelFormat.Bgra8, width, height, WinBitmapAlphaMode.Premultiplied)) {
            result = await ocr.RecognizeAsync(bitmap).AsTask(cancellation).ConfigureAwait(false);
        }
        RecognizedText text = new RecognizedText {
            Region = clipped,
            Language = ocr.RecognizerLanguage?.LanguageTag ?? ""
        };
        foreach(WinOcrLine line in result.Lines) {
            RecognizedLine recognizedLine = new RecognizedLine { Text = line.Text ?? "" };
            foreach(WinOcrWord word in line.Words) {
                global::Windows.Foundation.Rect box = word.BoundingRect;
                PixelRect bounds = OcrLayout.ToImage(box.X, box.Y, box.Width, box.Height, clipped, scaleX, scaleY);
                recognizedLine.Words.Add(new RecognizedWord { Text = word.Text ?? "", Bounds = bounds });
            }
            recognizedLine.Bounds = OcrLayout.Union(recognizedLine.Words.Select(w => w.Bounds));
            text.Lines.Add(recognizedLine);
        }
        Log.Info($"Texterkennung ({text.Language}): {text.Lines.Count} Zeile(n) in {clipped.Width}×{clipped.Height} bei {clipped.X},{clipped.Y}, Faktor {scale:0.###}, {watch.ElapsedMilliseconds} ms");
        return text;
    }

    /// <summary>Erkennung für die Sprachen des Benutzerprofils (einmal angelegt); null = nicht verfügbar (protokolliert).</summary>
    private WinOcrEngine? getEngine() {
        lock(engineSync) {
            if(engineChecked) return engine;
            engineChecked = true;
            try {
                engine = WinOcrEngine.TryCreateFromUserProfileLanguages();
                if(engine == null) {
                    Log.Info("Texterkennung nicht verfügbar: Windows hat für keine Sprache des Benutzerprofils eine OCR-Erkennung installiert");
                } else {
                    Log.Info($"Texterkennung bereit: {engine.RecognizerLanguage?.LanguageTag}");
                }
            } catch(Exception ex) {
                Log.Warn($"Texterkennung nicht verfügbar: {ex.GetType().Name}: {ex.Message}");
                engine = null;
            }
            return engine;
        }
    }

    /// <summary>Pixel des Ausschnitts als Bgra32 (eingefrorene Bitmaps sind threadübergreifend lesbar).</summary>
    private static byte[] copyPixels(BitmapSource original, PixelRect rect) {
        BitmapSource source = original.Format == PixelFormats.Bgra32 || original.Format == PixelFormats.Pbgra32 || original.Format == PixelFormats.Bgr32
            ? original
            : new FormatConvertedBitmap(original, PixelFormats.Bgra32, null, 0);
        int stride = rect.Width * 4;
        byte[] pixels = new byte[stride * rect.Height];
        source.CopyPixels(new Int32Rect(rect.X, rect.Y, rect.Width, rect.Height), pixels, stride, 0);
        if(source.Format == PixelFormats.Bgr32) {
            // Alpha-Kanal ist dort undefiniert: deckend setzen
            for(int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
        }
        return pixels;
    }
}
