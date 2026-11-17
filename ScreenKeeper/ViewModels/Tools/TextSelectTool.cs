using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Text auswählen“: markiert erkannten Text (OCR) wie in einem Dokument. Ziehen markiert die Wörter vom Start-
/// bis zum Endpunkt in Lesereihenfolge (auch über Zeilen), Doppelklick ein Wort, ein Klick ohne Ziehen hebt die
/// Markierung auf. Strg+C kopiert den markierten Text, Strg+A markiert alles (über das Fenster). Die Markierung ist reiner
/// Ansichtszustand: kein Objekt, nicht im Export, nicht im Rückgängig-Stapel. Ohne Erkennungsergebnis tut das Werkzeug
/// nichts.
/// </summary>
public sealed class TextSelectTool : IEditorTool {
    public const string ToolKey = "textselect";

    /// <summary>So weit (Gerätepixel) muss die Maus wandern, bevor aus einem Klick ein Ziehen wird.</summary>
    private const double dragThresholdDevice = 3;

    /// <summary>Zuschlag (Gerätepixel) um ein Wort beim Doppelklick.</summary>
    private const double wordToleranceDevice = 4;

    private readonly EditorViewModel editor;
    private OcrTextSelection selection = new OcrTextSelection(null);
    private int? anchor;
    private bool dragging;
    private bool moved;
    private double downX;
    private double downY;

    public TextSelectTool(EditorViewModel editor) {
        this.editor = editor;
    }

    public string Key => ToolKey;

    public string ToolTip => Strings.ToolTextSelect;

    // Textcursor (I-Beam) mit kleinen Querstrichen oben und unten
    public string IconData => "M7,3 H13 M10,3 V17 M7,17 H13 M4,7 V13 M16,7 V13";

    public bool IsGestureActive => dragging;

    public PixelPoint? MagnifierFocus => null;

    /// <summary>Ist Text markiert?</summary>
    public bool HasSelection => current.HasSelection;

    /// <summary>Gibt es erkannte Wörter (sonst tut das Werkzeug nichts)?</summary>
    public bool HasText => current.WordCount > 0;

    /// <summary>Markierter Text (Zeilen durch CRLF getrennt); leer = nichts markiert.</summary>
    public string SelectedText => current.GetSelectedText(Environment.NewLine);

    /// <summary>Hervorhebung je Zeile (Bildpixel).</summary>
    public IReadOnlyList<PixelRect> Highlights => current.GetHighlightRects();

    /// <summary>Markierung zum aktuellen Erkennungsergebnis (ein neues Ergebnis verwirft die alte Markierung).</summary>
    private OcrTextSelection current {
        get {
            if(!ReferenceEquals(selection.Source, editor.RecognizedText)) {
                selection = new OcrTextSelection(editor.RecognizedText);
                anchor = null;
                dragging = false;
            }
            return selection;
        }
    }

    public void Activate() {
    }

    public void Deactivate() {
        CancelGesture();
        ClearSelection();
    }

    /// <summary>Alles markieren; false, wenn es keinen erkannten Text gibt.</summary>
    public bool SelectAll() {
        OcrTextSelection target = current;
        if(target.WordCount == 0) return false;
        target.SelectAll();
        editor.NotifyOverlayChanged();
        return true;
    }

    /// <summary>Markierung aufheben; false, wenn nichts markiert war.</summary>
    public bool ClearSelection() {
        OcrTextSelection target = current;
        if(!target.HasSelection) return false;
        target.Clear();
        editor.NotifyOverlayChanged();
        return true;
    }

    public void PointerDown(PointerInput input) {
        OcrTextSelection target = current;
        if(target.WordCount == 0) return;
        if(input.ClickCount >= 2) {
            dragging = false;
            int? word = target.FindWordAt(input.X, input.Y, wordToleranceDevice / Math.Max(0.01, editor.DevicePixelsPerImagePixel));
            if(word.HasValue) {
                target.SelectRange(word.Value, word.Value);
            } else {
                target.Clear();
            }
            editor.NotifyOverlayChanged();
            return;
        }
        target.Clear();
        anchor = target.FindNearestWord(input.X, input.Y);
        dragging = anchor.HasValue;
        moved = false;
        downX = input.X;
        downY = input.Y;
        editor.NotifyOverlayChanged();
    }

    public void PointerMove(PointerInput input) {
        if(!dragging || !anchor.HasValue) return;
        if(!moved) {
            double threshold = dragThresholdDevice / Math.Max(0.01, editor.DevicePixelsPerImagePixel);
            if(Math.Abs(input.X - downX) < threshold && Math.Abs(input.Y - downY) < threshold) return;
            moved = true;
        }
        OcrTextSelection target = current;
        int? word = target.FindNearestWord(input.X, input.Y);
        if(!word.HasValue) return;
        target.SelectRange(anchor.Value, word.Value);
        editor.NotifyOverlayChanged();
    }

    public void PointerUp(PointerInput input) {
        PointerMove(input);
        dragging = false;
        anchor = null;
    }

    public void CancelGesture() {
        dragging = false;
        anchor = null;
    }

    public EditorCursor GetCursor(PointerInput input) {
        return EditorCursor.IBeam;
    }
}
