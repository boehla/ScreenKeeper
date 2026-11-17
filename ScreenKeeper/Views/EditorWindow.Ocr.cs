using System.Windows.Threading;
using ScreenKeeper.Models;
using ScreenKeeper.Services;

namespace ScreenKeeper.Views;

/// <summary>
/// Texterkennung im Editor: nach dem ersten Zeichnen und nach jeder Bereichsänderung (verzögert, damit Ziehen und
/// Rückgängig nicht jedes Mal eine Erkennung starten) im Hintergrund für den Bereich bzw. das ganze Bild. Ein Ergebnis
/// für einen inzwischen geänderten Bereich wird verworfen. Beim Schließen übernimmt <see cref="CaptureWorkflow"/> eine
/// noch fehlende Erkennung und schreibt ihren Text danach in die JSON. Ist die Erkennung ausgeschaltet, wird nur ein
/// durch eine Bereichsänderung veralteter <c>ocrText</c> neu erkannt (bzw. bei Fehlschlag entfernt). Fehler und fehlende
/// OCR-Sprachen erscheinen nur im Protokoll.
/// </summary>
public partial class EditorWindow {
    private static readonly TimeSpan ocrDelay = TimeSpan.FromMilliseconds(500);

    private DispatcherTimer? ocrTimer;
    private CancellationTokenSource? ocrCancellation;
    private Task<RecognizedText?>? ocrTask;
    private PixelRect ocrTaskRegion;
    // beim Schließen (vor dem Speichern) entschieden: Text im Hintergrund noch erkennen
    private bool ocrNeededAfterClose;

    private bool ocrAvailable => !warmUp && AppServices.IsInitialized;

    /// <summary>Einstellung „Text erkennen (OCR)“.</summary>
    private static bool ocrSettingEnabled => AppServices.Settings.Settings.RecognizeText;

    /// <summary>Erkennung (neu) einplanen, wenn für den aktuellen Bereich eine nötig ist und noch keine läuft.</summary>
    private void scheduleOcr() {
        if(closed || !ocrAvailable) return;
        if(!viewModel.ShouldRecognize(ocrSettingEnabled)) {
            // nichts zu tun (z. B. Rückgängig auf den alten Bereich): eine laufende Erkennung ist überflüssig
            cancelOcr();
            return;
        }
        if(ocrTask != null && ocrTaskRegion == viewModel.OcrTargetRegion) return;
        if(ocrTimer == null) {
            ocrTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher) { Interval = ocrDelay };
            ocrTimer.Tick += (s, e) => startOcr();
        }
        ocrTimer.Stop();
        ocrTimer.Start();
    }

    private async void startOcr() {
        ocrTimer?.Stop();
        if(closed || !ocrAvailable || !viewModel.ShouldRecognize(ocrSettingEnabled)) return;
        if(viewModel.IsGestureActive || viewModel.PreviewSelection.HasValue) {
            // während eines Ziehvorgangs warten, bis der Bereich feststeht
            ocrTimer?.Start();
            return;
        }
        PixelRect region = viewModel.OcrTargetRegion;
        if(ocrTask != null && ocrTaskRegion == region) return;
        cancelOcr();
        CancellationTokenSource cancellation = new CancellationTokenSource();
        Task<RecognizedText?> task = AppServices.Ocr.RecognizeAsync(original, region, cancellation.Token);
        ocrCancellation = cancellation;
        ocrTask = task;
        ocrTaskRegion = region;
        RecognizedText? result = await task;
        bool cancelled = cancellation.IsCancellationRequested;
        if(ReferenceEquals(ocrTask, task)) {
            ocrTask = null;
            ocrCancellation = null;
        }
        cancellation.Dispose();
        // nach dem Schließen kümmert sich der Ablauf um das Ergebnis (finishOcrOnClose)
        if(closed || cancelled) return;
        if(result == null) {
            // nicht verfügbar oder fehlgeschlagen: ein veralteter Text bleibt nicht stehen (nur bei ausgeschalteter Erkennung)
            if(region == viewModel.OcrTargetRegion) viewModel.OnOcrFailed(ocrSettingEnabled);
            return;
        }
        viewModel.ApplyOcrResult(result);
    }

    private void cancelOcr() {
        ocrTimer?.Stop();
        ocrCancellation?.Cancel();
        ocrCancellation = null;
        ocrTask = null;
    }

    /// <summary>
    /// Beim Schließen vor dem Speichern: ein schon fertiges, passendes Ergebnis ins Dokument übernehmen und entscheiden,
    /// ob der Text danach noch im Hintergrund erkannt werden muss (ein veralteter Text wird dann schon jetzt entfernt).
    /// </summary>
    private void prepareOcrForClose() {
        ocrNeededAfterClose = false;
        if(!ocrAvailable) return;
        Task<RecognizedText?>? task = ocrTask;
        if(task != null && task.IsCompletedSuccessfully && task.Result != null) viewModel.ApplyOcrResult(task.Result);
        ocrNeededAfterClose = viewModel.PrepareOcrTextForClose(ocrSettingEnabled);
    }

    /// <summary>
    /// Nach dem Speichern beim Schließen: muss der Text noch erkannt werden (siehe <see cref="prepareOcrForClose"/>), läuft
    /// die Erkennung (bzw. die schon laufende) im Hintergrund weiter und nur ihr Text wird danach in die JSON geschrieben.
    /// Wurde nur geöffnet und der Bereich nicht geändert, gilt der vorhandene Text weiter.
    /// </summary>
    private void finishOcrOnClose() {
        ocrTimer?.Stop();
        if(!ocrAvailable || !ocrNeededAfterClose || string.IsNullOrEmpty(jsonPath)) {
            cancelOcr();
            return;
        }
        PixelRect region = viewModel.OcrTargetRegion;
        Task<RecognizedText?>? running = ocrTask != null && ocrTaskRegion == region ? ocrTask : null;
        if(running == null) {
            cancelOcr();
        } else {
            // nicht abbrechen: der Ablauf wartet auf genau diese Erkennung
            ocrTask = null;
            ocrCancellation = null;
        }
        // auch bei ausgeschalteter Erkennung (dann war der Text veraltet und ist schon entfernt)
        AppServices.Workflow.RecognizeAndStore(jsonPath, original, region, running, true);
    }
}
