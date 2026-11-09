using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Textmarker“: halbtransparentes Rechteck aufziehen (Standardfarbe Gelb).
/// </summary>
public sealed class HighlightTool : DrawingToolBase {
    public const string ToolKey = "highlight";

    public HighlightTool(EditorViewModel editor) : base(editor) {
    }

    public override string Key => ToolKey;

    public override string ToolTip => Strings.ToolHighlight;

    public override string IconData => "M3,17 H17 M5,14 L7,10 L13,4 L16,7 L10,13 Z";

    public override string AnnotationType => AnnotationTypes.Highlight;

    protected override Annotation CreateAnnotation(PixelPoint start) {
        return new HighlightAnnotation {
            Rect = new PixelRect(start.X, start.Y, 1, 1),
            Color = Editor.Options.HighlightColor
        };
    }

    protected override void UpdateAnnotation(Annotation annotation, PixelPoint start, PixelPoint current, PointerInput input) {
        ((HighlightAnnotation)annotation).Rect = GetRect(start, current, false);
    }

    protected override bool IsValid(Annotation annotation) {
        return IsLargeEnough(((HighlightAnnotation)annotation).Rect);
    }
}
