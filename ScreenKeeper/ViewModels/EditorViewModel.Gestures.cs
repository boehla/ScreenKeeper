using ScreenKeeper.Models;
using ScreenKeeper.ViewModels.Tools;

namespace ScreenKeeper.ViewModels;

/// <summary>Was ein Rechtsklick auf die Fläche bewirkt hat (siehe <see cref="EditorViewModel.RightClick"/>).</summary>
public enum RightClickResult {
    /// <summary>Nichts (anderes Werkzeug als „Bereich“).</summary>
    None,
    /// <summary>Laufender Vorgang abgebrochen (Aufziehen, Anfasser, Verschieben, Tastatur-Auswahl).</summary>
    Cancelled,
    /// <summary>Zum Werkzeug „Auswahl“ gewechselt.</summary>
    SwitchedToSelect
}

/// <summary>Welche Stufe ein Druck auf Esc ausgelöst hat (siehe <see cref="EditorViewModel.Escape"/>).</summary>
public enum EscapeResult {
    /// <summary>Laufender Vorgang abgebrochen.</summary>
    Cancelled,
    /// <summary>Textmarkierung von „Text auswählen“ aufgehoben.</summary>
    TextSelectionCleared,
    /// <summary>Von einem anderen Werkzeug zu „Auswahl“ gewechselt (ab 0.16).</summary>
    SwitchedToSelect,
    /// <summary>Nichts mehr aufzuheben: das Fenster schließt den Editor.</summary>
    Close
}

/// <summary>
/// Vorschau des nächsten Nummern-Stempels am Zeiger: Nummer, Farbe, Durchmesser und Mittelpunkt (Bildpixel, wie beim
/// echten Setzen auf das Bild begrenzt).
/// </summary>
public sealed record StampPreview(int Number, string Color, double Size, PixelPoint Center);

/// <summary>
/// Stempel, an dem der Tastenhinweis „+/-: Nummer“ steht: Mittelpunkt und Durchmesser in Bildpixeln. An der
/// halbtransparenten Vorschau nennt der Hinweis zusätzlich die Nummer (<paramref name="Number"/>, sonst null).
/// </summary>
public sealed record StampHintTarget(double X, double Y, double Size, int? Number = null);

/// <summary>
/// Rechtsklick, Esc-Stufen und die Stempel-Vorschau (ab 0.16). Die Entscheidungen liegen hier, damit sie ohne Fenster
/// testbar sind; das Fenster kümmert sich nur um Mausfang, Pan und Schließen.
/// </summary>
public sealed partial class EditorViewModel {
    /// <summary>
    /// Rechtsklick auf die Fläche. Nur beim Werkzeug „Bereich“: ein laufender Vorgang (<paramref name="pointerCaptured"/>
    /// oder <see cref="IsGestureActive"/>) wird abgebrochen, sonst wird zu „Auswahl“ gewechselt (auch mit vorhandenem
    /// Bereich). Bei anderen Werkzeugen passiert nichts.
    /// </summary>
    public RightClickResult RightClick(bool pointerCaptured) {
        if(!ReferenceEquals(activeTool, SelectionTool)) return RightClickResult.None;
        if(pointerCaptured || IsGestureActive) {
            CancelGesture();
            return RightClickResult.Cancelled;
        }
        SelectTool(ObjectSelectTool);
        return RightClickResult.SwitchedToSelect;
    }

    /// <summary>
    /// Esc (das Verschieben der Ansicht beendet vorher das Fenster): laufenden Vorgang abbrechen, sonst Textmarkierung
    /// aufheben, sonst von einem anderen Werkzeug zu „Auswahl“ wechseln; bei „Auswahl“ oder „Bereich“
    /// <see cref="EscapeResult.Close"/> – auch mit ausgewähltem Objekt (bis 0.15 hob Esc erst die Auswahl auf).
    /// </summary>
    public EscapeResult Escape() {
        if(IsGestureActive) {
            CancelGesture();
            return EscapeResult.Cancelled;
        }
        if(ReferenceEquals(activeTool, TextSelectTool) && TextSelectTool.ClearSelection()) return EscapeResult.TextSelectionCleared;
        if(!IsSelectingToolActive) {
            SelectTool(ObjectSelectTool);
            return EscapeResult.SwitchedToSelect;
        }
        return EscapeResult.Close;
    }

    /// <summary>
    /// Esc im Textfeld: Text übernehmen (wie <see cref="CommitTextEdit"/>, der Text geht nicht verloren) und direkt zu
    /// „Auswahl“ wechseln; der Text bleibt dort ausgewählt.
    /// </summary>
    public void CommitTextEditAndSelect() {
        CommitTextEdit();
        SelectTool(ObjectSelectTool);
    }

    /// <summary>
    /// Vorschau des nächsten Stempels am Zeiger oder null. Sichtbar nur beim Werkzeug „Nummern-Stempel“, solange der
    /// Zeiger frei über der Fläche ist (<paramref name="pointerFree"/>: Maus über der Fläche, kein Verschieben der Ansicht)
    /// und dort ein Klick einen Stempel setzen würde – über dem greifbaren ausgewählten Stempel gilt Verschieben bzw.
    /// Größe ändern (dann ist der Zeiger kein Fadenkreuz).
    /// </summary>
    public StampPreview? GetStampPreview(PointerInput input, bool pointerFree) {
        if(!pointerFree || activeTool is not NumberStampTool || IsTextEditing || selectedObject.IsActive) return null;
        if(GetCursor(input) != EditorCursor.Cross) return null;
        PixelPoint center = SelectionGeometry.ClampPoint(input.Pixel, ImageBounds);
        return new StampPreview(NextStampNumber, Options.Color, Options.StampSize, center);
    }

    /// <summary>
    /// Wo der Tastenhinweis „+/-: Nummer“ steht – immer an dem Stempel, den die Tasten gerade ändern: am ausgewählten
    /// Stempel (nicht, solange er gezogen wird), sonst an der Vorschau (<paramref name="preview"/>, null = nicht zu
    /// sehen). Null: kein Hinweis.
    /// </summary>
    public StampHintTarget? GetStampHintTarget(StampPreview? preview) {
        if(CanChangeStampNumber && SelectedAnnotation is NumberStampAnnotation stamp) {
            return selectedObject.IsActive ? null : new StampHintTarget(stamp.X, stamp.Y, stamp.Size);
        }
        if(preview != null && CanChangeNextStampNumber) return new StampHintTarget(preview.Center.X, preview.Center.Y, preview.Size, preview.Number);
        return null;
    }
}
