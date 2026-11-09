using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Bereich“: Aufziehen, 8 Anfasser, Verschieben im Bereich, Tastatur-Auswahl (Start-/Endpunkt).
/// Während eines Ziehvorgangs wird nur <see cref="EditorViewModel.PreviewSelection"/> geändert; beim Loslassen kommt
/// genau ein <see cref="Undo.SetSelectionCommand"/> auf den Stapel.
/// Zusätzlich wählt ein Klick auf ein Zeichenobjekt dieses aus (statt einen Bereich aufzuziehen) und verschiebt bzw.
/// ändert es über <see cref="ObjectManipulator"/>. Reihenfolge: Anfasser/Körper des ausgewählten Objekts, Bereichs-Anfasser
/// und -Kanten, andere Objekte, Inneres des Bereichs, Leerraum (hebt die Objektauswahl auf).
/// </summary>
public sealed class SelectionTool : IEditorTool {
    public const string ToolKey = "region";

    /// <summary>Kantenlänge der Anfasser in Gerätepixeln.</summary>
    public const double HandleSizeDevice = 8;

    private const double handleHitExtraDevice = 3;
    private const double edgeHitDevice = 4;

    private enum Gesture {
        None,
        /// <summary>Taste gedrückt, Maus noch nicht auf einen anderen Pixel bewegt (Klick ohne Ziehen tut nichts).</summary>
        Pending,
        Create,
        Resize,
        Move,
        /// <summary>Ein Zeichenobjekt wird ausgewählt, verschoben oder in der Größe geändert.</summary>
        Object
    }

    private readonly EditorViewModel editor;
    private readonly ObjectManipulator objects;
    private Gesture gesture;
    private PixelPoint downPixel;
    private PixelRect? originalSelection;
    private HandleDrag handleDrag;
    private bool keyboardActive;
    private PixelPoint keyboardAnchor;
    private PixelPoint focus;

    public SelectionTool(EditorViewModel editor) {
        this.editor = editor;
        objects = new ObjectManipulator(editor);
    }

    public string Key => ToolKey;

    public string ToolTip => Strings.ToolRegion;

    public string IconData => "M3,8 V3 H8 M12,3 H17 V8 M17,12 V17 H12 M8,17 H3 V12";

    public bool IsGestureActive => gesture != Gesture.None || keyboardActive;

    /// <summary>Läuft eine Tastatur-Auswahl (Startpunkt gesetzt, Endpunkt fehlt)?</summary>
    public bool IsKeyboardSelecting => keyboardActive;

    /// <summary>Wird gerade der ganze Bereich verschoben?</summary>
    public bool IsMoving => gesture == Gesture.Move;

    public PixelPoint? MagnifierFocus {
        get {
            if(gesture == Gesture.Create || gesture == Gesture.Resize || keyboardActive) return focus;
            if((gesture == Gesture.None || gesture == Gesture.Pending) && !editor.Selection.HasValue) return focus;
            return null;
        }
    }

    public void Activate() {
        focus = editor.CursorPixel;
    }

    public void Deactivate() {
        CancelGesture();
    }

    public void PointerDown(PointerInput input) {
        if(keyboardActive) {
            keyboardActive = false;
            editor.PreviewSelection = null;
        }
        PixelPoint pixel = SelectionGeometry.ClampPoint(input.Pixel, editor.ImageBounds);
        downPixel = pixel;
        focus = pixel;
        originalSelection = editor.Selection;
        if(objects.TryBeginOnSelected(input)) {
            gesture = Gesture.Object;
            return;
        }
        SelectionHandle handle = hitTest(input);
        bool regionEdge = handle != SelectionHandle.None && handle != SelectionHandle.Move && originalSelection.HasValue;
        if(!regionEdge && objects.TryBeginOnAny(input)) {
            gesture = Gesture.Object;
            return;
        }
        editor.SelectAnnotation(null);
        if(handle == SelectionHandle.Move) {
            gesture = Gesture.Move;
        } else if(handle != SelectionHandle.None && originalSelection.HasValue) {
            gesture = Gesture.Resize;
            handleDrag = HandleDrag.Begin(originalSelection.Value, handle, pixel);
            (_, focus) = handleDrag.Update(pixel, editor.ImageBounds);
        } else {
            gesture = Gesture.Pending;
        }
        editor.NotifyOverlayChanged();
    }

    public void PointerMove(PointerInput input) {
        if(gesture == Gesture.Object) {
            objects.Move(input);
            return;
        }
        PixelPoint pixel = SelectionGeometry.ClampPoint(input.Pixel, editor.ImageBounds);
        PixelRect? preview = editor.PreviewSelection;
        PixelPoint newFocus = pixel;
        switch(gesture) {
            case Gesture.None:
                if(keyboardActive) preview = SelectionGeometry.FromPoints(keyboardAnchor, pixel);
                break;
            case Gesture.Pending:
                if(pixel != downPixel) {
                    gesture = Gesture.Create;
                    preview = SelectionGeometry.FromPoints(downPixel, pixel);
                }
                break;
            case Gesture.Create:
                preview = SelectionGeometry.FromPoints(downPixel, pixel);
                break;
            case Gesture.Resize:
                (PixelRect resized, PixelPoint edge) = handleDrag.Update(input.Pixel, editor.ImageBounds);
                preview = resized;
                newFocus = edge;
                break;
            case Gesture.Move:
                if(originalSelection.HasValue) preview = SelectionGeometry.MoveWithin(originalSelection.Value, pixel.X - downPixel.X, pixel.Y - downPixel.Y, editor.ImageBounds);
                break;
        }
        if(newFocus == focus && preview == editor.PreviewSelection) return;
        focus = newFocus;
        editor.PreviewSelection = preview;
        editor.NotifyOverlayChanged();
    }

    public void PointerUp(PointerInput input) {
        if(gesture == Gesture.None) return;
        if(gesture == Gesture.Object) {
            gesture = Gesture.None;
            objects.Up(input);
            return;
        }
        PointerMove(input);
        Gesture finished = gesture;
        PixelRect? result = editor.PreviewSelection;
        gesture = Gesture.None;
        editor.PreviewSelection = null;
        if(finished != Gesture.Pending && result.HasValue) {
            editor.CommitSelection(originalSelection, result);
        }
        editor.NotifyOverlayChanged();
    }

    /// <summary>
    /// Leertaste/Enter: erster Druck setzt den Startpunkt am Cursor, der zweite den Endpunkt (Bereich = beide Pixel inklusive).
    /// </summary>
    public void KeyboardMark(PixelPoint cursor) {
        if(gesture != Gesture.None) return;
        PixelPoint pixel = SelectionGeometry.ClampPoint(cursor, editor.ImageBounds);
        focus = pixel;
        if(!keyboardActive) {
            keyboardActive = true;
            keyboardAnchor = pixel;
            editor.PreviewSelection = SelectionGeometry.FromPoints(pixel, pixel);
            editor.NotifyOverlayChanged();
            return;
        }
        keyboardActive = false;
        editor.PreviewSelection = null;
        editor.CommitSelection(editor.Selection, SelectionGeometry.FromPoints(keyboardAnchor, pixel));
        editor.NotifyOverlayChanged();
    }

    public void CancelGesture() {
        if(gesture == Gesture.None && !keyboardActive) return;
        if(gesture == Gesture.Object) objects.Cancel();
        gesture = Gesture.None;
        keyboardActive = false;
        editor.PreviewSelection = null;
        editor.NotifyOverlayChanged();
    }

    public EditorCursor GetCursor(PointerInput input) {
        switch(gesture) {
            case Gesture.Object: return objects.GetCursor(input) ?? EditorCursor.SizeAll;
            case Gesture.Move: return EditorCursor.SizeAll;
            case Gesture.Resize: return SelectionGeometry.GetCursor(handleDrag.Handle);
            case Gesture.Pending:
            case Gesture.Create: return EditorCursor.Cross;
        }
        if(keyboardActive) return EditorCursor.Cross;
        EditorCursor? objectCursor = objects.GetCursor(input);
        if(objectCursor.HasValue) return objectCursor.Value;
        SelectionHandle handle = hitTest(input);
        bool regionEdge = handle != SelectionHandle.None && handle != SelectionHandle.Move;
        if(!regionEdge && editor.HitTestAnnotation(input) != null) return EditorCursor.SizeAll;
        return SelectionGeometry.GetCursor(handle);
    }

    private SelectionHandle hitTest(PointerInput input) {
        PixelRect? selection = editor.Selection;
        if(!selection.HasValue) return SelectionHandle.None;
        double devicePerPixel = Math.Max(0.01, editor.DevicePixelsPerImagePixel);
        double handleRadius = (HandleSizeDevice / 2 + handleHitExtraDevice) / devicePerPixel;
        double edgeTolerance = edgeHitDevice / devicePerPixel;
        return SelectionGeometry.HitTest(selection.Value, input.X, input.Y, handleRadius, edgeTolerance);
    }
}
