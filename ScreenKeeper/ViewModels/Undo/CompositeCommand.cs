namespace ScreenKeeper.ViewModels.Undo;

/// <summary>
/// Mehrere Befehle als ein Rückgängig-Schritt (Ausführen in Reihenfolge, Rückgängig in umgekehrter Reihenfolge).
/// </summary>
public sealed class CompositeCommand : EditorCommand {
    private readonly List<IUndoableCommand> commands;
    private readonly string description;

    public CompositeCommand(string description, IEnumerable<IUndoableCommand> commands) {
        this.description = description;
        this.commands = commands.ToList();
    }

    public IReadOnlyList<IUndoableCommand> Commands => commands;

    public override string Description => description;

    public override DocumentChangeKind Changes {
        get {
            DocumentChangeKind kind = DocumentChangeKind.None;
            foreach(IUndoableCommand command in commands) {
                if(command is EditorCommand editorCommand) kind |= editorCommand.Changes;
            }
            return kind;
        }
    }

    public override void Do() {
        foreach(IUndoableCommand command in commands) command.Do();
    }

    public override void Undo() {
        for(int i = commands.Count - 1; i >= 0; i--) commands[i].Undo();
    }
}
