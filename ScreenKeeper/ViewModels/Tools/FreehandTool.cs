using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Freihand“: sammelt beim Ziehen Punkte (nur, wenn sich die Maus mindestens <see cref="MinPointDistanceDevice"/>
/// Gerätepixel bewegt hat) und dünnt sie beim Loslassen aus; Farbe, Strichstärke, Glättung.
/// </summary>
public sealed class FreehandTool : DrawingToolBase {
    public const string ToolKey = "freehand";

    /// <summary>Mindestabstand zum letzten Punkt in Gerätepixeln.</summary>
    public const double MinPointDistanceDevice = 2;

    /// <summary>Toleranz beim Ausdünnen in Bildpixeln (Abweichung, die nicht sichtbar ist).</summary>
    public const double SimplifyTolerance = 0.75;

    public FreehandTool(EditorViewModel editor) : base(editor) {
    }

    public override string Key => ToolKey;

    public override string ToolTip => Strings.ToolFreehand;

    public override string IconData => "M3,14 C5,6 8,6 9,11 S13,16 17,6";

    public override string AnnotationType => AnnotationTypes.Freehand;

    protected override Annotation CreateAnnotation(PixelPoint start) {
        return new FreehandAnnotation {
            Points = new List<PixelPoint> { start },
            Stroke = Editor.Options.Color,
            Thickness = Editor.Options.Thickness,
            Smooth = Editor.Options.Smooth
        };
    }

    protected override void UpdateAnnotation(Annotation annotation, PixelPoint start, PixelPoint current, PointerInput input) {
        FreehandAnnotation freehand = (FreehandAnnotation)annotation;
        PixelPoint last = freehand.Points[freehand.Points.Count - 1];
        double minDistance = Math.Max(1, MinPointDistanceDevice / Math.Max(0.01, Editor.DevicePixelsPerImagePixel));
        double dx = current.X - last.X;
        double dy = current.Y - last.Y;
        if(Math.Sqrt(dx * dx + dy * dy) >= minDistance) freehand.Points.Add(current);
    }

    protected override void Complete(Annotation annotation) {
        FreehandAnnotation freehand = (FreehandAnnotation)annotation;
        freehand.Points = AnnotationGeometry.Simplify(freehand.Points, SimplifyTolerance);
    }

    protected override bool IsValid(Annotation annotation) {
        FreehandAnnotation freehand = (FreehandAnnotation)annotation;
        if(freehand.Points.Count < 2) return false;
        PixelRect bounds = AnnotationGeometry.GetBounds(freehand.Points);
        return bounds.Width >= MinSize || bounds.Height >= MinSize;
    }
}
