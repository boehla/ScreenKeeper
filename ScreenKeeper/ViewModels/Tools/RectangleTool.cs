using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Rechteck“: aufziehen (Shift: Quadrat); Rahmenfarbe, Strichstärke, Füllung optional.
/// </summary>
public sealed class RectangleTool : DrawingToolBase {
    public const string ToolKey = "rectangle";

    public RectangleTool(EditorViewModel editor) : base(editor) {
    }

    public override string Key => ToolKey;

    public override string ToolTip => Strings.ToolRectangle;

    public override string IconData => "M3,5 H17 V15 H3 Z";

    public override string AnnotationType => AnnotationTypes.Rectangle;

    protected override Annotation CreateAnnotation(PixelPoint start) {
        ToolOptions options = Editor.Options;
        return new RectangleAnnotation {
            Rect = new PixelRect(start.X, start.Y, 1, 1),
            Stroke = options.Color,
            Thickness = options.Thickness,
            Fill = options.Fill ? options.Color : null
        };
    }

    protected override void UpdateAnnotation(Annotation annotation, PixelPoint start, PixelPoint current, PointerInput input) {
        ((RectangleAnnotation)annotation).Rect = GetRect(start, current, input.Shift);
    }

    protected override bool IsValid(Annotation annotation) {
        return IsLargeEnough(((RectangleAnnotation)annotation).Rect);
    }
}
