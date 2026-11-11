using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Text“: Klick setzt die Position eines freien Texts (der Text beginnt am Klickpunkt und wächst mit dem
/// Inhalt), Aufziehen legt wie beim Rechteck ein festes Textfeld fest (Breite und Höhe; Shift: Quadrat). Danach startet
/// die Inline-Bearbeitung (<see cref="EditorViewModel.BeginTextEdit"/>). Ein Klick auf einen vorhandenen Text öffnet
/// dessen Bearbeitung.
/// </summary>
public sealed class TextTool : IAnnotationTool {
    public const string ToolKey = "text";

    /// <summary>
    /// Mindestbreite und -höhe eines aufgezogenen Textfelds in Bildpixeln; kleinere Rahmen (versehentliches Ziehen beim
    /// Klicken) ergeben einen freien Text an der linken oberen Ecke.
    /// </summary>
    public const int MinFieldSize = 10;

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
        editor.ToolPreviewRect = DrawingToolBase.GetRect(start, pixel, input.Shift);
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
        // beim Aufziehen des Felds das Kreuz wie beim Rechteck
        return state == State.Dragging ? EditorCursor.Cross : EditorCursor.IBeam;
    }

    /// <summary>
    /// Neuer (noch leerer) Text mit den aktuellen Optionen. Ohne <paramref name="box"/> (oder bei einer Box unter
    /// <see cref="MinFieldSize"/>) ein freier Text am Punkt <paramref name="point"/> mit fester Schriftgröße; mit Box ein
    /// festes Feld genau in der Box (automatische Schriftgröße nach den Optionen).
    /// </summary>
    public TextAnnotation CreateText(PixelPoint point, PixelRect? box) {
        ToolOptions options = editor.Options;
        double padding = TextLayout.GetPadding(options.FontSize);
        TextAnnotation text = new TextAnnotation {
            FontFamily = TextLayout.DefaultFontFamily,
            FontSize = options.FontSize,
            Foreground = options.Color,
            Background = options.TextBackground ? AnnotationColors.GetTextBackground(options.Color) : null,
            Alignment = options.TextAlignment
        };
        if(box.HasValue && box.Value.Width >= MinFieldSize && box.Value.Height >= MinFieldSize) {
            text.X = box.Value.X;
            text.Y = box.Value.Y;
            text.Width = box.Value.Width;
            text.Height = box.Value.Height;
            text.AutoFontSize = options.TextAutoFontSize;
        } else {
            text.X = point.X - (int)padding;
            text.Y = point.Y - (int)padding;
        }
        return text;
    }
}
