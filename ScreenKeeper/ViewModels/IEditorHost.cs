using ScreenKeeper.Models;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Was das <see cref="EditorViewModel"/> vom Fenster braucht (damit es ohne Fenster testbar bleibt).
/// </summary>
public interface IEditorHost {
    /// <summary>Aktuelle Mausposition in Bildpixeln (nicht begrenzt).</summary>
    PixelPoint GetCursorImagePoint();

    /// <summary>„Speichern unter“-Dialog; null bei Abbruch.</summary>
    string? AskSaveAsPath(string initialFolder, string fileName);

    void ShowError(string message);

    /// <summary>Editor schließen (der Bearbeitungsstand wird beim Schließen gespeichert).</summary>
    void CloseEditor();
}
