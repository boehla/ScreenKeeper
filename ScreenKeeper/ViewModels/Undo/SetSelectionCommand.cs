using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels.Undo;

/// <summary>
/// Setzt <see cref="CaptureDocument.Selection"/> (Aufziehen, Anfasser, Verschieben, Tastatur – ein Befehl je Vorgang).
/// </summary>
public sealed class SetSelectionCommand : EditorCommand {
    private readonly CaptureDocument document;

    public SetSelectionCommand(CaptureDocument document, PixelRect? oldSelection, PixelRect? newSelection) {
        this.document = document;
        OldSelection = oldSelection;
        NewSelection = newSelection;
    }

    public PixelRect? OldSelection { get; }

    public PixelRect? NewSelection { get; }

    public override string Description => Strings.UndoSelection;

    public override DocumentChangeKind Changes => DocumentChangeKind.Selection;

    public override void Do() {
        document.Selection = NewSelection;
    }

    public override void Undo() {
        document.Selection = OldSelection;
    }
}
