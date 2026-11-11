using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.ViewModels.Undo;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Verschieben und Größe-Ändern des ausgewählten Zeichenobjekts (genutzt von „Auswahl“, implizit von „Bereich“ und vom
/// <see cref="EditorViewModel"/> für das zuletzt gezeichnete, noch ausgewählte Objekt bei aktivem Zeichenwerkzeug).
/// Während des Ziehens wird das Objekt im Dokument durch eine geänderte Kopie ersetzt (Live-Vorschau); beim Loslassen
/// kommt genau ein <see cref="ChangeAnnotationCommand"/> auf den Stapel, Abbrechen stellt den alten Stand wieder her.
/// </summary>
public sealed class ObjectManipulator {
    private enum Mode {
        None,
        /// <summary>Gedrückt, aber noch nicht bewegt (ein Klick wählt nur aus).</summary>
        Pending,
        Move,
        Resize
    }

    private readonly EditorViewModel editor;
    private Mode mode;
    private Annotation? before;
    private ObjectHandle handle;
    private PixelPoint downPixel;

    public ObjectManipulator(EditorViewModel editor) {
        this.editor = editor;
    }

    public bool IsActive => mode != Mode.None;

    /// <summary>
    /// Anfasser oder Körper des ausgewählten Objekts unter der Maus (<see cref="ObjectHandle.None"/>: nichts davon).
    /// </summary>
    public ObjectHandle HitSelected(PointerInput input) {
        Annotation? selected = editor.SelectedAnnotation;
        if(selected == null) return ObjectHandle.None;
        ObjectHandle hit = AnnotationEditing.HitHandle(selected, input.X, input.Y, editor.HandleRadius, editor.Measure);
        if(hit != ObjectHandle.None) return hit;
        return AnnotationHitTesting.HitTest(selected, input.X, input.Y, editor.HitTolerance, editor.Measure) ? ObjectHandle.Move : ObjectHandle.None;
    }

    /// <summary>
    /// Beginnt einen Vorgang am ausgewählten Objekt (Anfasser oder Körper); false, wenn es nicht getroffen wurde.
    /// </summary>
    public bool TryBeginOnSelected(PointerInput input) {
        ObjectHandle hit = HitSelected(input);
        if(hit == ObjectHandle.None) return false;
        begin(editor.SelectedAnnotation!, hit, input);
        return true;
    }

    /// <summary>
    /// Wählt das oberste Objekt unter der Maus aus und beginnt das Verschieben; false, wenn keins getroffen wurde.
    /// </summary>
    public bool TryBeginOnAny(PointerInput input) {
        Annotation? hit = editor.HitTestAnnotation(input);
        if(hit == null) return false;
        editor.SelectAnnotation(hit);
        begin(hit, ObjectHandle.Move, input);
        return true;
    }

    public void Move(PointerInput input) {
        if(mode == Mode.None || before == null) return;
        if(mode == Mode.Pending) {
            if(input.Pixel == downPixel) return;
            mode = Mode.Move;
        }
        Annotation updated = mode == Mode.Move
            ? AnnotationEditing.Moved(before, input.Pixel.X - downPixel.X, input.Pixel.Y - downPixel.Y)
            : AnnotationEditing.Resized(before, handle, downPixel, input.Pixel, input.Shift, editor.Measure);
        AnnotationList.Replace(editor.Document, updated);
        editor.NotifyAnnotationsChanged();
    }

    /// <summary>
    /// Vorgang beenden (ein Rückgängig-Schritt, wenn sich das Objekt geändert hat). True, wenn es nur ein Klick auf den
    /// Körper war (ohne Bewegung).
    /// </summary>
    public bool Up(PointerInput input) {
        if(mode == Mode.None || before == null) return false;
        Move(input);
        Mode finished = mode;
        Annotation original = before;
        mode = Mode.None;
        before = null;
        if(finished == Mode.Pending) {
            editor.NotifyOverlayChanged();
            return true;
        }
        Annotation? current = AnnotationList.Find(editor.Document, original.Id);
        if(current == null || current.ToJson() == original.ToJson()) {
            editor.NotifyOverlayChanged();
            return false;
        }
        string description = finished == Mode.Move ? Strings.UndoMoveAnnotation : Strings.UndoResizeAnnotation;
        editor.UndoStack.Push(new ChangeAnnotationCommand(editor.Document, original, current, description));
        return false;
    }

    public void Cancel() {
        if(mode == Mode.None || before == null) return;
        Mode cancelled = mode;
        Annotation original = before;
        mode = Mode.None;
        before = null;
        if(cancelled != Mode.Pending) {
            AnnotationList.Replace(editor.Document, original.Clone());
            editor.NotifyAnnotationsChanged();
        }
    }

    /// <summary>
    /// Mauszeiger während eines Vorgangs bzw. über dem ausgewählten Objekt; null, wenn das Objekt nicht betroffen ist.
    /// </summary>
    public EditorCursor? GetCursor(PointerInput input) {
        if(mode == Mode.Move || mode == Mode.Pending) return EditorCursor.SizeAll;
        if(mode == Mode.Resize) return AnnotationEditing.GetCursor(handle);
        ObjectHandle hit = HitSelected(input);
        return hit == ObjectHandle.None ? null : AnnotationEditing.GetCursor(hit);
    }

    private void begin(Annotation annotation, ObjectHandle hit, PointerInput input) {
        before = annotation.Clone();
        handle = hit;
        downPixel = input.Pixel;
        mode = hit == ObjectHandle.Move ? Mode.Pending : Mode.Resize;
        editor.NotifyOverlayChanged();
    }
}
