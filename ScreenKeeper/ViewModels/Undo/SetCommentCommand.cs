using ScreenKeeper.Models;
using ScreenKeeper.Resources;

namespace ScreenKeeper.ViewModels.Undo;

/// <summary>
/// Setzt <see cref="CaptureDocument.Comment"/> (ein Befehl je übernommener Eingabe im Kommentar-Dialog).
/// </summary>
public sealed class SetCommentCommand : EditorCommand {
    private readonly CaptureDocument document;

    public SetCommentCommand(CaptureDocument document, string? oldComment, string? newComment) {
        this.document = document;
        OldComment = oldComment;
        NewComment = newComment;
    }

    public string? OldComment { get; }

    public string? NewComment { get; }

    public override string Description => Strings.UndoComment;

    public override DocumentChangeKind Changes => DocumentChangeKind.Comment;

    public override void Do() {
        document.Comment = NewComment;
    }

    public override void Undo() {
        document.Comment = OldComment;
    }
}
