using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;
using ScreenKeeper.ViewModels.Tools;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Texterkennung im Editor: welcher Ausschnitt erkannt werden soll, ob das Ergebnis im Speicher dazu passt, Übernahme
/// eines Ergebnisses (ohne Rückgängig-Schritt – es ist aus dem Bild abgeleitet) und das Werkzeug „Text auswählen“.
/// Das Ergebnis mit der Lage der Wörter (<see cref="RecognizedText"/>) gibt es nur hier im Arbeitsspeicher; ins Dokument
/// geht nur der Text (<see cref="CaptureDocument.OcrText"/>). Gestartet wird die Erkennung vom Fenster (im Hintergrund,
/// verzögert), beim Öffnen immer neu.
/// </summary>
public sealed partial class EditorViewModel {
    private RecognizedText? recognizedText;
    // Ausschnitt, zu dem Document.OcrText gehört; null = keiner bekannt
    private PixelRect? ocrTextRegion;

    /// <summary>Wird ausgelöst, wenn ein neues Erkennungsergebnis übernommen wurde.</summary>
    public event EventHandler? RecognizedTextChanged;

    public TextSelectTool TextSelectTool => (TextSelectTool)FindTool(TextSelectTool.ToolKey)!;

    /// <summary>Letztes übernommenes Erkennungsergebnis mit Lage der Wörter (nur im Speicher); null = noch keins.</summary>
    public RecognizedText? RecognizedText => recognizedText;

    /// <summary>Ausschnitt, den die Erkennung abdecken soll: der Bereich, ohne Bereich das ganze Bild.</summary>
    public PixelRect OcrTargetRegion => StorageService.GetCropRegion(Document, ImageBounds.Width, ImageBounds.Height);

    /// <summary>Fehlt im Speicher ein Ergebnis für <see cref="OcrTargetRegion"/> (keins oder für einen anderen Ausschnitt)?</summary>
    public bool NeedsOcr => recognizedText == null || recognizedText.Region != OcrTargetRegion;

    /// <summary>
    /// Beim Schließen: passt <see cref="CaptureDocument.OcrText"/> nicht (mehr) zum gespeicherten Ausschnitt? Dann muss
    /// noch im Hintergrund erkannt werden. Ein beim Öffnen vorhandener Text gilt für den Bereich beim Öffnen.
    /// </summary>
    public bool NeedsOcrTextOnClose => ocrTextRegion != OcrTargetRegion;

    /// <summary>
    /// Ein gespeicherter Text (<see cref="CaptureDocument.OcrText"/> nicht null) passt nicht mehr zum Bereich – er darf
    /// so nicht stehen bleiben, auch wenn die Erkennung ausgeschaltet ist.
    /// </summary>
    public bool HasStaleOcrText => Document.OcrText != null && NeedsOcrTextOnClose;

    /// <summary>
    /// Soll im Editor (neu) erkannt werden? Eingeschaltet: wenn im Speicher kein Ergebnis für den Bereich ist.
    /// Ausgeschaltet: nur, wenn ein gespeicherter Text nach einer Bereichsänderung veraltet ist.
    /// </summary>
    public bool ShouldRecognize(bool enabled) {
        if(!NeedsOcr) return false;
        return enabled || HasStaleOcrText;
    }

    /// <summary>
    /// Die Erkennung ist fehlgeschlagen bzw. nicht verfügbar (nicht abgebrochen). Bei ausgeschalteter Erkennung wird ein
    /// veralteter Text entfernt (null) statt stehen zu bleiben; eingeschaltet bleibt alles, wie es ist.
    /// </summary>
    public void OnOcrFailed(bool enabled) {
        if(enabled || !HasStaleOcrText) return;
        Document.OcrText = null;
        ocrTextRegion = null;
        Log.Info("Texterkennung nicht möglich: veralteter erkannter Text entfernt");
    }

    /// <summary>
    /// Beim Schließen vor dem Speichern: muss der Text im Hintergrund noch erkannt werden? Eingeschaltet: wenn
    /// <see cref="CaptureDocument.OcrText"/> nicht zum Bereich passt. Ausgeschaltet: nur bei veraltetem Text – der wird
    /// dann schon jetzt entfernt (null), damit er auch bei fehlschlagender Erkennung nicht stehen bleibt.
    /// </summary>
    public bool PrepareOcrTextForClose(bool enabled) {
        if(enabled) return NeedsOcrTextOnClose;
        if(!HasStaleOcrText) return false;
        Document.OcrText = null;
        ocrTextRegion = null;
        return true;
    }

    /// <summary>
    /// Ergebnis übernehmen, wenn es zum aktuellen Ausschnitt passt (sonst wurde der Bereich inzwischen geändert und eine
    /// neue Erkennung folgt). Hält es im Speicher und setzt <see cref="CaptureDocument.OcrText"/>.
    /// </summary>
    public bool ApplyOcrResult(RecognizedText result) {
        if(result.Region != OcrTargetRegion) return false;
        recognizedText = result;
        ocrTextRegion = result.Region;
        Document.OcrText = result.GetText();
        RecognizedTextChanged?.Invoke(this, EventArgs.Empty);
        NotifyOverlayChanged();
        return true;
    }

    /// <summary>Beim Öffnen: ein gespeicherter Text gehört zum gespeicherten Bereich (bzw. ohne Bereich zum ganzen Bild).</summary>
    private void initializeOcr() {
        ocrTextRegion = Document.OcrText != null ? OcrTargetRegion : null;
    }

    /// <summary>Markierter Text des Werkzeugs „Text auswählen“, solange es aktiv ist; sonst leer.</summary>
    public string SelectedRecognizedText => ReferenceEquals(ActiveTool, TextSelectTool) ? TextSelectTool.SelectedText : "";

    /// <summary>
    /// Strg+C bei „Text auswählen“: markierten Text in die Zwischenablage (der Editor bleibt offen). False, wenn nichts
    /// markiert ist – dann gilt Strg+C wie sonst (Bereich als Bild).
    /// </summary>
    public bool CopySelectedRecognizedText() {
        string text = SelectedRecognizedText;
        if(text.Length == 0) return false;
        try {
            AppServices.Clipboard.SetText(text);
            Log.Info($"Zwischenablage: markierter Text ({text.Length} Zeichen)");
        } catch(Exception ex) {
            Log.Error(ex, "Text konnte nicht in die Zwischenablage kopiert werden");
            host.ShowError(Strings.Format(Strings.ErrorClipboardFailed, ex.Message));
        }
        return true;
    }

    /// <summary>Hervorhebung des markierten Texts (Bildpixel), solange „Text auswählen“ aktiv ist.</summary>
    public IReadOnlyList<PixelRect> GetTextHighlights() {
        return ReferenceEquals(ActiveTool, TextSelectTool) ? TextSelectTool.Highlights : Array.Empty<PixelRect>();
    }
}
