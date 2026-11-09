using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Ellipse“: aufziehen (Shift: Kreis); Rahmenfarbe, Strichstärke, Füllung optional.
/// </summary>
public sealed class EllipseTool : DrawingToolBase {
    public const string ToolKey = "ellipse";

    public EllipseTool(EditorViewModel editor) : base(editor) {
    }

    public override string Key => ToolKey;

    public override string ToolTip => Strings.ToolEllipse;

    public override string IconData => "M3,10 A7,6 0 1 1 17,10 A7,6 0 1 1 3,10 Z";

    public override string AnnotationType => AnnotationTypes.Ellipse;

    protected override Annotation CreateAnnotation(PixelPoint start) {
        ToolOptions options = Editor.Options;
        return new EllipseAnnotation {
            Rect = new PixelRect(start.X, start.Y, 1, 1),
            Stroke = options.Color,
            Thickness = options.Thickness,
            Fill = options.Fill ? options.Color : null
        };
    }

    protected override void UpdateAnnotation(Annotation annotation, PixelPoint start, PixelPoint current, PointerInput input) {
        ((EllipseAnnotation)annotation).Rect = GetRect(start, current, input.Shift);
    }

    protected override bool IsValid(Annotation annotation) {
        return IsLargeEnough(((EllipseAnnotation)annotation).Rect);
    }
}
