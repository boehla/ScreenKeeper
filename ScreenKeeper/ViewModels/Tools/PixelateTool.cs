using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Werkzeug „Verpixeln“: Rechteck aufziehen; Blockgröße einstellbar. Wird aus dem Original berechnet.
/// </summary>
public sealed class PixelateTool : DrawingToolBase {
    public const string ToolKey = "pixelate";

    public PixelateTool(EditorViewModel editor) : base(editor) {
    }

    public override string Key => ToolKey;

    public override string ToolTip => Strings.ToolPixelate;

    public override string IconData => "M3,3 H17 V17 H3 Z M3,8 H17 M3,12.5 H17 M8,3 V17 M12.5,3 V17";

    public override string AnnotationType => AnnotationTypes.Pixelate;

    protected override Annotation CreateAnnotation(PixelPoint start) {
        return new PixelateAnnotation {
            Rect = new PixelRect(start.X, start.Y, 1, 1),
            BlockSize = Editor.Options.BlockSize
        };
    }

    protected override void UpdateAnnotation(Annotation annotation, PixelPoint start, PixelPoint current, PointerInput input) {
        ((PixelateAnnotation)annotation).Rect = GetRect(start, current, false);
    }

    protected override bool IsValid(Annotation annotation) {
        return IsLargeEnough(((PixelateAnnotation)annotation).Rect);
    }
}
