using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Linie“: von Start- zu Endpunkt ziehen (Shift: 45°-Raster); Farbe, Strichstärke, Linienenden.
/// </summary>
public sealed class LineTool : DrawingToolBase {
    public const string ToolKey = "line";

    public LineTool(EditorViewModel editor) : base(editor) {
    }

    public override string Key => ToolKey;

    public override string ToolTip => Strings.ToolLine;

    public override string IconData => "M4,16 L16,4";

    public override string AnnotationType => AnnotationTypes.Line;

    protected override Annotation CreateAnnotation(PixelPoint start) {
        return new LineAnnotation {
            Points = new List<PixelPoint> { start, start },
            Stroke = Editor.Options.Color,
            Thickness = Editor.Options.Thickness,
            StartCap = Editor.Options.LineStartCap,
            EndCap = Editor.Options.LineEndCap
        };
    }

    protected override void UpdateAnnotation(Annotation annotation, PixelPoint start, PixelPoint current, PointerInput input) {
        PixelPoint end = input.Shift ? AnnotationGeometry.SnapAngle(start, current) : current;
        ((LineAnnotation)annotation).Points = new List<PixelPoint> { start, end };
    }

    protected override bool IsValid(Annotation annotation) {
        return IsLongEnough(((LineAnnotation)annotation).Points);
    }
}
