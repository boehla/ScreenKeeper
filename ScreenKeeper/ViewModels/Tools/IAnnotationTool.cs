using System.Windows.Input;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Ein Zeichenwerkzeug, das Objekte eines bestimmten Typs erzeugt. Der Typ bestimmt, welche Optionen die Werkzeugleiste
/// zeigt (Farbe, Strichstärke …).
/// </summary>
public interface IAnnotationTool : IEditorTool {
    /// <summary>Typ der erzeugten Objekte (<see cref="Models.AnnotationTypes"/>).</summary>
    string AnnotationType { get; }
}

/// <summary>
/// Tastenkürzel der Werkzeuge (nur ohne Strg/Alt und nur, wenn gerade kein Text bearbeitet wird).
/// </summary>
public static class ToolShortcuts {
    private static readonly Dictionary<Key, string> keys = new Dictionary<Key, string> {
        { Key.S, ObjectSelectTool.ToolKey },
        { Key.R, RectangleTool.ToolKey },
        { Key.E, EllipseTool.ToolKey },
        { Key.L, LineTool.ToolKey },
        { Key.A, ArrowTool.ToolKey },
        { Key.F, FreehandTool.ToolKey },
        { Key.T, TextTool.ToolKey },
        { Key.H, HighlightTool.ToolKey },
        { Key.P, PixelateTool.ToolKey },
        { Key.B, BlurTool.ToolKey },
        { Key.N, NumberStampTool.ToolKey },
        { Key.M, CursorTool.ToolKey }
    };

    /// <summary>Werkzeug-Schlüssel zur Taste; null, wenn die Taste kein Werkzeug wählt.</summary>
    public static string? GetToolKey(Key key) {
        return keys.TryGetValue(key, out string? toolKey) ? toolKey : null;
    }
}
