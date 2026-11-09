namespace ScreenKeeper.ViewModels.Undo;

public enum UndoStackAction {
    Execute,
    Push,
    Undo,
    Redo,
    Clear
}

public sealed class UndoStackChangedEventArgs : EventArgs {
    public UndoStackChangedEventArgs(UndoStackAction action, IUndoableCommand? command) {
        Action = action;
        Command = command;
    }

    public UndoStackAction Action { get; }

    /// <summary>Der betroffene Befehl (null bei <see cref="UndoStackAction.Clear"/>).</summary>
    public IUndoableCommand? Command { get; }
}

/// <summary>
/// Rückgängig/Wiederholen-Stapel mit begrenzter Kapazität (die ältesten Schritte fallen heraus).
/// Ein neuer Befehl verwirft alle wiederholbaren Schritte.
/// </summary>
public sealed class UndoStack {
    public const int DefaultCapacity = 200;

    private readonly LinkedList<IUndoableCommand> undoList = new LinkedList<IUndoableCommand>();
    private readonly Stack<IUndoableCommand> redoStack = new Stack<IUndoableCommand>();

    public UndoStack(int capacity = DefaultCapacity) {
        if(capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public int Capacity { get; }

    public int UndoCount => undoList.Count;

    public int RedoCount => redoStack.Count;

    public bool CanUndo => undoList.Count > 0;

    public bool CanRedo => redoStack.Count > 0;

    public string? UndoDescription => undoList.Last?.Value.Description;

    public string? RedoDescription => redoStack.Count > 0 ? redoStack.Peek().Description : null;

    public event EventHandler<UndoStackChangedEventArgs>? Changed;

    /// <summary>
    /// Führt den Befehl aus und legt ihn auf den Stapel.
    /// </summary>
    public void Execute(IUndoableCommand command) {
        command.Do();
        add(command);
        Changed?.Invoke(this, new UndoStackChangedEventArgs(UndoStackAction.Execute, command));
    }

    /// <summary>
    /// Legt einen bereits ausgeführten Befehl auf den Stapel (z. B. nach einem Ziehvorgang mit Live-Vorschau).
    /// </summary>
    public void Push(IUndoableCommand command) {
        add(command);
        Changed?.Invoke(this, new UndoStackChangedEventArgs(UndoStackAction.Push, command));
    }

    public bool Undo() {
        LinkedListNode<IUndoableCommand>? last = undoList.Last;
        if(last == null) return false;
        undoList.RemoveLast();
        last.Value.Undo();
        redoStack.Push(last.Value);
        Changed?.Invoke(this, new UndoStackChangedEventArgs(UndoStackAction.Undo, last.Value));
        return true;
    }

    public bool Redo() {
        if(redoStack.Count == 0) return false;
        IUndoableCommand command = redoStack.Pop();
        command.Do();
        undoList.AddLast(command);
        Changed?.Invoke(this, new UndoStackChangedEventArgs(UndoStackAction.Redo, command));
        return true;
    }

    public void Clear() {
        undoList.Clear();
        redoStack.Clear();
        Changed?.Invoke(this, new UndoStackChangedEventArgs(UndoStackAction.Clear, null));
    }

    private void add(IUndoableCommand command) {
        redoStack.Clear();
        undoList.AddLast(command);
        while(undoList.Count > Capacity) undoList.RemoveFirst();
    }
}
