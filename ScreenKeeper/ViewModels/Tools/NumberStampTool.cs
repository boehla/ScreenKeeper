using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.ViewModels.Undo;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Nummern-Stempel“: Klick setzt einen Stempel (Mittelpunkt am Klickpunkt) mit der nächsten Nummer
/// (größte vorhandene + 1). Beim Löschen wird nicht umnummeriert.
/// </summary>
public sealed class NumberStampTool : IAnnotationTool {
    public const string ToolKey = "number";

    private readonly EditorViewModel editor;
    private bool pending;
    private PixelPoint downPixel;

    public NumberStampTool(EditorViewModel editor) {
        this.editor = editor;
    }

    public string Key => ToolKey;

    public string ToolTip => Strings.ToolNumber;

    public string IconData => "M3,10 A7,7 0 1 1 17,10 A7,7 0 1 1 3,10 Z M8.5,7.5 L10.5,6 V14";

    public string AnnotationType => AnnotationTypes.NumberStamp;

    public bool IsGestureActive => pending;

    public PixelPoint? MagnifierFocus => null;

    /// <summary>
    /// Nächste Nummer: größte vorhandene Nummer + 1 (ohne Stempel: 1).
    /// </summary>
    public static int GetNextNumber(IEnumerable<Annotation> annotations) {
        int max = 0;
        foreach(Annotation annotation in annotations) {
            if(annotation is NumberStampAnnotation stamp) max = Math.Max(max, stamp.Number);
        }
        return max + 1;
    }

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
        NumberStampAnnotation stamp = new NumberStampAnnotation {
            X = downPixel.X,
            Y = downPixel.Y,
            Number = GetNextNumber(editor.Document.Annotations),
            Color = editor.Options.Color,
            Size = editor.Options.StampSize
        };
        editor.UndoStack.Execute(new AddAnnotationCommand(editor.Document, stamp));
        editor.SelectAnnotation(stamp);
    }

    public void CancelGesture() {
        pending = false;
    }

    public EditorCursor GetCursor(PointerInput input) {
        return EditorCursor.Cross;
    }
}
