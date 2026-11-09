using ScreenKeeper.Models;
using ScreenKeeper.ViewModels.Undo;

namespace ScreenKeeper.Tests;

public class UndoStackTests {
    private sealed class CounterCommand : IUndoableCommand {
        private readonly List<int> target;
        private readonly int value;

        public CounterCommand(List<int> target, int value) {
            this.target = target;
            this.value = value;
        }

        public int DoCount { get; private set; }

        public string Description => $"+{value}";

        public void Do() {
            DoCount++;
            target.Add(value);
        }

        public void Undo() {
            target.RemoveAt(target.Count - 1);
        }
    }

    [Fact]
    public void Execute_Undo_Redo_RestoresState() {
        List<int> values = new List<int>();
        UndoStack stack = new UndoStack();
        Assert.False(stack.CanUndo);
        Assert.False(stack.CanRedo);

        stack.Execute(new CounterCommand(values, 1));
        stack.Execute(new CounterCommand(values, 2));
        Assert.Equal(new[] { 1, 2 }, values);
        Assert.Equal("+2", stack.UndoDescription);

        Assert.True(stack.Undo());
        Assert.Equal(new[] { 1 }, values);
        Assert.True(stack.CanRedo);
        Assert.Equal("+2", stack.RedoDescription);

        Assert.True(stack.Undo());
        Assert.Empty(values);
        Assert.False(stack.Undo());

        Assert.True(stack.Redo());
        Assert.True(stack.Redo());
        Assert.False(stack.Redo());
        Assert.Equal(new[] { 1, 2 }, values);
    }

    [Fact]
    public void NewCommand_DiscardsRedo() {
        List<int> values = new List<int>();
        UndoStack stack = new UndoStack();
        stack.Execute(new CounterCommand(values, 1));
        stack.Execute(new CounterCommand(values, 2));
        stack.Undo();
        Assert.Equal(1, stack.RedoCount);

        stack.Execute(new CounterCommand(values, 3));
        Assert.False(stack.CanRedo);
        Assert.Equal(new[] { 1, 3 }, values);
    }

    [Fact]
    public void Capacity_DropsOldestSteps() {
        List<int> values = new List<int>();
        UndoStack stack = new UndoStack(100);
        for(int i = 0; i < 150; i++) stack.Execute(new CounterCommand(values, i));
        Assert.Equal(100, stack.UndoCount);
        int undone = 0;
        while(stack.Undo()) undone++;
        Assert.Equal(100, undone);
        // die ersten 50 Schritte sind nicht mehr rückgängig zu machen
        Assert.Equal(Enumerable.Range(0, 50), values);
    }

    [Fact]
    public void DefaultCapacity_IsAtLeast100() {
        Assert.True(new UndoStack().Capacity >= 100);
        Assert.Throws<ArgumentOutOfRangeException>(() => new UndoStack(0));
    }

    [Fact]
    public void Push_DoesNotExecuteAgain() {
        List<int> values = new List<int>();
        UndoStack stack = new UndoStack();
        CounterCommand command = new CounterCommand(values, 5);
        command.Do();
        stack.Push(command);
        Assert.Equal(1, command.DoCount);
        stack.Undo();
        Assert.Empty(values);
        stack.Redo();
        Assert.Equal(2, command.DoCount);
    }

    [Fact]
    public void Changed_ReportsActionAndCommand() {
        List<(UndoStackAction, IUndoableCommand?)> events = new List<(UndoStackAction, IUndoableCommand?)>();
        UndoStack stack = new UndoStack();
        stack.Changed += (s, e) => events.Add((e.Action, e.Command));
        CounterCommand command = new CounterCommand(new List<int>(), 1);
        stack.Execute(command);
        stack.Undo();
        stack.Redo();
        stack.Clear();
        Assert.Equal(new (UndoStackAction, IUndoableCommand?)[] {
            (UndoStackAction.Execute, command),
            (UndoStackAction.Undo, command),
            (UndoStackAction.Redo, command),
            (UndoStackAction.Clear, null)
        }, events);
        Assert.False(stack.CanUndo);
    }

    [Fact]
    public void SetSelectionCommand_SetsAndRestoresSelection() {
        CaptureDocument document = new CaptureDocument { Selection = new PixelRect(1, 2, 3, 4) };
        UndoStack stack = new UndoStack();
        stack.Execute(new SetSelectionCommand(document, document.Selection, new PixelRect(10, 20, 30, 40)));
        Assert.Equal(new PixelRect(10, 20, 30, 40), document.Selection);
        stack.Undo();
        Assert.Equal(new PixelRect(1, 2, 3, 4), document.Selection);
        stack.Redo();
        Assert.Equal(new PixelRect(10, 20, 30, 40), document.Selection);
    }

    [Fact]
    public void CompositeCommand_UndoesInReverseOrder() {
        List<int> values = new List<int>();
        CompositeCommand composite = new CompositeCommand("beides", new IUndoableCommand[] { new CounterCommand(values, 1), new CounterCommand(values, 2) });
        UndoStack stack = new UndoStack();
        stack.Execute(composite);
        Assert.Equal(new[] { 1, 2 }, values);
        Assert.Equal(1, stack.UndoCount);
        stack.Undo();
        Assert.Empty(values);
        Assert.Equal(DocumentChangeKind.None, composite.Changes);
        CompositeCommand withSelection = new CompositeCommand("x", new IUndoableCommand[] { new SetSelectionCommand(new CaptureDocument(), null, new PixelRect(0, 0, 1, 1)) });
        Assert.Equal(DocumentChangeKind.Selection, withSelection.Changes);
    }
}
