using ScreenKeeper.Models;
using ScreenKeeper.Resources;
using ScreenKeeper.Services;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Pfeil“: vom Anfang zur Spitze ziehen (Shift: 45°-Raster); Farbe, Strichstärke, Linienenden.
/// </summary>
public sealed class ArrowTool : DrawingToolBase {
    public const string ToolKey = "arrow";

    public ArrowTool(EditorViewModel editor) : base(editor) {
    }

    public override string Key => ToolKey;

    public override string ToolTip => Strings.ToolArrow;

    public override string IconData => "M4,16 L15,5 M8.5,5 H15 V11.5";

    public override string AnnotationType => AnnotationTypes.Arrow;

    protected override Annotation CreateAnnotation(PixelPoint start) {
        return new ArrowAnnotation {
            Points = new List<PixelPoint> { start, start },
            Stroke = Editor.Options.Color,
            Thickness = Editor.Options.Thickness,
            StartCap = Editor.Options.ArrowStartCap,
            EndCap = Editor.Options.ArrowEndCap
        };
    }

    protected override void UpdateAnnotation(Annotation annotation, PixelPoint start, PixelPoint current, PointerInput input) {
        PixelPoint end = input.Shift ? AnnotationGeometry.SnapAngle(start, current) : current;
        ((ArrowAnnotation)annotation).Points = new List<PixelPoint> { start, end };
    }

    protected override bool IsValid(Annotation annotation) {
        return IsLongEnough(((ArrowAnnotation)annotation).Points);
    }
}
