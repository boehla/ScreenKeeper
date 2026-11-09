using System.Windows.Media;

namespace ScreenKeeper.ViewModels.Tools;

/// <summary>
/// Ein Werkzeug-Knopf in der Werkzeugleiste (Abschnitt „tools“).
/// </summary>
public sealed class ToolItem : ViewModelBase {
    private bool isActive;

    public ToolItem(IEditorTool tool) {
        Tool = tool;
        Geometry icon = Geometry.Parse(tool.IconData);
        icon.Freeze();
        Icon = icon;
    }

    public IEditorTool Tool { get; }

    public string ToolTip => Tool.ToolTip;

    public Geometry Icon { get; }

    public bool IsActive {
        get => isActive;
        set => SetProperty(ref isActive, value);
    }
}
