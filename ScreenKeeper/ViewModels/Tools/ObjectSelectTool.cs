using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Auswahl / Verschieben“: Objekt anklicken (oberstes zuerst), verschieben, an den Anfassern in der Größe
/// ändern; Klick auf Leerraum hebt die Auswahl auf. Entf löscht (über das Fenster), Doppelklick auf Text bearbeitet ihn.
/// </summary>
public sealed class ObjectSelectTool : IEditorTool {
    public const string ToolKey = "object";

    private readonly EditorViewModel editor;
    private readonly ObjectManipulator objects;

    public ObjectSelectTool(EditorViewModel editor) {
        this.editor = editor;
        objects = new ObjectManipulator(editor);
    }

    public string Key => ToolKey;

    public string ToolTip => Strings.ToolObjectSelect;

    public string IconData => "M5,2.5 V16 L8.5,12.5 L11,18 L13,17 L10.5,11.5 L15,11.5 Z";

    public bool IsGestureActive => objects.IsActive;

    public PixelPoint? MagnifierFocus => null;

    public void Activate() {
    }

    public void Deactivate() {
        CancelGesture();
    }

    public void PointerDown(PointerInput input) {
        if(objects.TryBeginOnSelected(input)) return;
        if(objects.TryBeginOnAny(input)) return;
        editor.SelectAnnotation(null);
    }

    public void PointerMove(PointerInput input) {
        objects.Move(input);
    }

    public void PointerUp(PointerInput input) {
        objects.Up(input);
    }

    public void CancelGesture() {
        objects.Cancel();
    }

    public EditorCursor GetCursor(PointerInput input) {
        EditorCursor? cursor = objects.GetCursor(input);
        if(cursor.HasValue) return cursor.Value;
        return editor.HitTestAnnotation(input) != null ? EditorCursor.SizeAll : EditorCursor.Arrow;
    }
}
