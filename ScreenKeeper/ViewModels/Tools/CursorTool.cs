using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.ViewModels.Undo;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Virtuelle Maus“: Klick setzt einen Mauszeiger (Hotspot am Klickpunkt) mit der in der Werkzeugleiste
/// gewählten Art (aufgenommener Zeiger, Pfeil, Hand, Textcursor). Beliebig oft einfügbar.
/// </summary>
public sealed class CursorTool : IAnnotationTool {
    public const string ToolKey = "cursor";

    private readonly EditorViewModel editor;
    private bool pending;
    private PixelPoint downPixel;

    public CursorTool(EditorViewModel editor) {
        this.editor = editor;
    }

    public string Key => ToolKey;

    public string ToolTip => Strings.ToolCursor;

    public string IconData => "M10,2 C6.7,2 5,4 5,7 V13 C5,16 7,18 10,18 C13,18 15,16 15,13 V7 C15,4 13.3,2 10,2 Z M10,2 V8 M5,8 H15";

    public string AnnotationType => AnnotationTypes.Cursor;

    public bool IsGestureActive => pending;

    public PixelPoint? MagnifierFocus => null;

    public void Activate() {
    }

    public void Deactivate() {
        CancelGesture();
    }

    public void PointerDown(PointerInput input) {
        pending = true;
        downPixel = SelectionGeometry.ClampPoint(input.Pixel, editor.ImageBounds);
    }

    public void PointerMove(PointerInput input) {
    }

    public void PointerUp(PointerInput input) {
        if(!pending) return;
        pending = false;
        CursorAnnotation cursor = new CursorAnnotation {
            X = downPixel.X,
            Y = downPixel.Y,
            Kind = editor.EffectiveCursorKind,
            Visible = true
        };
        editor.UndoStack.Execute(new AddAnnotationCommand(editor.Document, cursor));
        editor.SelectAnnotation(cursor);
    }

    public void CancelGesture() {
        pending = false;
    }

    public EditorCursor GetCursor(PointerInput input) {
        return EditorCursor.Cross;
    }
}
