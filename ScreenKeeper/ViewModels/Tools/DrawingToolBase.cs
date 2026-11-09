using ScreenKeeper.Models;
using ScreenKeeper.ViewModels.Undo;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Gemeinsamer Ablauf der Werkzeuge, die ein Objekt aufziehen (Rechteck, Ellipse, Linie, Pfeil, Freihand, Textmarker,
/// Verpixeln, Weichzeichnen): Sobald sich die Maus nach dem Drücken bewegt, wird das Objekt angelegt und als
/// Live-Vorschau direkt in die Liste des Dokuments gestellt; beim Loslassen kommt genau ein
/// <see cref="AddAnnotationCommand"/> auf den Stapel – oder das Objekt wird wieder entfernt, wenn es die Mindestgröße
/// nicht erreicht. Abbrechen entfernt die Vorschau.
/// </summary>
public abstract class DrawingToolBase : IAnnotationTool {
    /// <summary>Mindestgröße (Breite und Höhe bzw. Länge) in Bildpixeln; kleinere Objekte werden verworfen.</summary>
    public const int MinSize = 3;

    private enum State {
        None,
        Pending,
        Drawing
    }

    private State state;
    private PixelPoint start;
    private Annotation? current;

    protected DrawingToolBase(EditorViewModel editor) {
        Editor = editor;
    }

    protected EditorViewModel Editor { get; }

    public abstract string Key { get; }

    public abstract string ToolTip { get; }

    public abstract string IconData { get; }

    public abstract string AnnotationType { get; }

    public bool IsGestureActive => state != State.None;

    public PixelPoint? MagnifierFocus => null;

    public virtual void Activate() {
    }

    public virtual void Deactivate() {
        CancelGesture();
    }

    public void PointerDown(PointerInput input) {
        CancelGesture();
        // Eine neue Zeichnung beginnt: die bisherige Objektauswahl endet
        Editor.SelectAnnotation(null);
        start = clamp(input.Pixel);
        state = State.Pending;
    }

    public void PointerMove(PointerInput input) {
        if(state == State.None) return;
        PixelPoint pixel = clamp(input.Pixel);
        if(state == State.Pending) {
            if(pixel == start) return;
            current = CreateAnnotation(start);
            Editor.Document.Annotations.Add(current);
            state = State.Drawing;
        }
        UpdateAnnotation(current!, start, pixel, input);
        Editor.NotifyAnnotationsChanged();
    }

    public void PointerUp(PointerInput input) {
        if(state == State.None) return;
        if(state == State.Pending) {
            state = State.None;
            return;
        }
        PointerMove(input);
        Annotation annotation = current!;
        state = State.None;
        current = null;
        Complete(annotation);
        if(IsValid(annotation)) {
            Editor.UndoStack.Push(new AddAnnotationCommand(Editor.Document, annotation));
            // Das zuletzt gezeichnete Objekt bleibt ausgewählt, damit Farbe und Optionen darauf wirken
            Editor.SelectAnnotation(annotation);
        } else {
            AnnotationList.Remove(Editor.Document, annotation.Id);
            Editor.NotifyAnnotationsChanged();
        }
    }

    public void CancelGesture() {
        if(state == State.None) return;
        Annotation? annotation = current;
        state = State.None;
        current = null;
        if(annotation != null) {
            AnnotationList.Remove(Editor.Document, annotation.Id);
            Editor.NotifyAnnotationsChanged();
        }
    }

    public virtual EditorCursor GetCursor(PointerInput input) {
        return EditorCursor.Cross;
    }

    /// <summary>Neues Objekt am Startpunkt (mit den aktuellen Werkzeug-Optionen).</summary>
    protected abstract Annotation CreateAnnotation(PixelPoint start);

    /// <summary>Objekt an die aktuelle Mausposition anpassen (Live-Vorschau).</summary>
    protected abstract void UpdateAnnotation(Annotation annotation, PixelPoint start, PixelPoint current, PointerInput input);

    /// <summary>Hat das fertige Objekt die Mindestgröße?</summary>
    protected abstract bool IsValid(Annotation annotation);

    /// <summary>Nachbearbeitung beim Loslassen (z. B. Punkte ausdünnen).</summary>
    protected virtual void Complete(Annotation annotation) {
    }

    /// <summary>
    /// Rechteck aus Start- und aktuellem Pixel (beide inklusive); mit <paramref name="square"/> quadratisch.
    /// </summary>
    protected static PixelRect GetRect(PixelPoint start, PixelPoint current, bool square) {
        if(square) {
            int dx = current.X - start.X;
            int dy = current.Y - start.Y;
            int size = Math.Max(Math.Abs(dx), Math.Abs(dy));
            current = new PixelPoint(start.X + (dx < 0 ? -size : size), start.Y + (dy < 0 ? -size : size));
        }
        return SelectionGeometry.FromPoints(start, current);
    }

    protected static bool IsLargeEnough(PixelRect rect) {
        return rect.Width >= MinSize && rect.Height >= MinSize;
    }

    protected static bool IsLongEnough(IReadOnlyList<PixelPoint> points) {
        if(points.Count < 2) return false;
        double dx = points[1].X - points[0].X;
        double dy = points[1].Y - points[0].Y;
        return Math.Sqrt(dx * dx + dy * dy) >= MinSize;
    }

    private PixelPoint clamp(PixelPoint pixel) {
        return SelectionGeometry.ClampPoint(pixel, Editor.ImageBounds);
    }
}
