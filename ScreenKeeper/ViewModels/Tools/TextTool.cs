using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Text“: Klick setzt die Position (der Text beginnt am Klickpunkt), Aufziehen legt zusätzlich die
/// Umbruchbreite fest. Danach startet die Inline-Bearbeitung (<see cref="EditorViewModel.BeginTextEdit"/>).
/// Ein Klick auf einen vorhandenen Text öffnet dessen Bearbeitung.
/// </summary>
public sealed class TextTool : IAnnotationTool {
    public const string ToolKey = "text";

    private enum State {
        None,
        Pending,
        Dragging
    }

    private readonly EditorViewModel editor;
    private State state;
    private PixelPoint start;

    public TextTool(EditorViewModel editor) {
        this.editor = editor;
    }

    public string Key => ToolKey;

    public string ToolTip => Strings.ToolText;

    public string IconData => "M4,5.5 V3 H16 V5.5 M10,3 V17 M7.5,17 H12.5";

    public string AnnotationType => AnnotationTypes.Text;

    public bool IsGestureActive => state != State.None;

    public PixelPoint? MagnifierFocus => null;

    public void Activate() {
    }

    public void Deactivate() {
        CancelGesture();
    }

    public void PointerDown(PointerInput input) {
        CancelGesture();
        start = SelectionGeometry.ClampPoint(input.Pixel, editor.ImageBounds);
        state = State.Pending;
    }

    public void PointerMove(PointerInput input) {
        if(state == State.None) return;
        PixelPoint pixel = SelectionGeometry.ClampPoint(input.Pixel, editor.ImageBounds);
        if(state == State.Pending) {
            if(pixel == start) return;
            state = State.Dragging;
        }
        editor.ToolPreviewRect = SelectionGeometry.FromPoints(start, pixel);
        editor.NotifyOverlayChanged();
    }

    public void PointerUp(PointerInput input) {
        if(state == State.None) return;
        if(state == State.Pending) {
            state = State.None;
            if(editor.HitTestAnnotation(input) is TextAnnotation existing) {
                editor.BeginTextEdit(existing, false);
                return;
            }
            editor.BeginTextEdit(CreateText(start, null), true);
            return;
        }
        PointerMove(input);
        PixelRect box = editor.ToolPreviewRect ?? SelectionGeometry.FromPoints(start, start);
        state = State.None;
        editor.ToolPreviewRect = null;
        editor.NotifyOverlayChanged();
        editor.BeginTextEdit(CreateText(new PixelPoint(box.X, box.Y), box), true);
    }

    public void CancelGesture() {
        if(state == State.None) return;
        state = State.None;
        if(editor.ToolPreviewRect.HasValue) {
            editor.ToolPreviewRect = null;
            editor.NotifyOverlayChanged();
        }
    }

    public EditorCursor GetCursor(PointerInput input) {
        return EditorCursor.IBeam;
    }

    /// <summary>
    /// Neuer (noch leerer) Text mit den aktuellen Optionen. Ohne <paramref name="box"/> beginnt der Text am Punkt
    /// <paramref name="point"/>; mit Box ist das die linke obere Ecke und die Box-Breite die Umbruchbreite.
    /// </summary>
    public TextAnnotation CreateText(PixelPoint point, PixelRect? box) {
        ToolOptions options = editor.Options;
        double padding = TextLayout.GetPadding(options.FontSize);
        TextAnnotation text = new TextAnnotation {
            FontFamily = TextLayout.DefaultFontFamily,
            FontSize = options.FontSize,
            Foreground = options.Color,
            Background = options.TextBackground ? AnnotationColors.GetTextBackground(options.Color) : null
        };
        if(box.HasValue && box.Value.Width >= options.FontSize + 2 * padding) {
            text.X = box.Value.X;
            text.Y = box.Value.Y;
            text.Width = (int)Math.Round(box.Value.Width - 2 * padding);
        } else {
            text.X = point.X - (int)padding;
            text.Y = point.Y - (int)padding;
        }
        return text;
    }
}
