using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Weichzeichnen“: Rechteck aufziehen; Stärke einstellbar. Wird aus dem Original berechnet.
/// </summary>
public sealed class BlurTool : DrawingToolBase {
    public const string ToolKey = "blur";

    public BlurTool(EditorViewModel editor) : base(editor) {
    }

    public override string Key => ToolKey;

    public override string ToolTip => Strings.ToolBlur;

    public override string IconData => "M10,3 C10,3 4,9.5 4,12.5 A6,6 0 0 0 16,12.5 C16,9.5 10,3 10,3 Z";

    public override string AnnotationType => AnnotationTypes.Blur;

    protected override Annotation CreateAnnotation(PixelPoint start) {
        return new BlurAnnotation {
            Rect = new PixelRect(start.X, start.Y, 1, 1),
            Radius = Editor.Options.BlurRadius
        };
    }

    protected override void UpdateAnnotation(Annotation annotation, PixelPoint start, PixelPoint current, PointerInput input) {
        ((BlurAnnotation)annotation).Rect = GetRect(start, current, false);
    }

    protected override bool IsValid(Annotation annotation) {
        return IsLargeEnough(((BlurAnnotation)annotation).Rect);
    }
}
