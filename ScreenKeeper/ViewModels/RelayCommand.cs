using System.Windows.Input;

namespace ScreenKeeper.ViewModels;

/// <summary>
/// Einfacher <see cref="ICommand"/> aus Delegaten.
/// </summary>
public sealed class RelayCommand : ICommand {
    private readonly Action<object?> execute;
    private readonly Func<object?, bool>? canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute == null ? null : _ => canExecute()) {
    }

    public RelayCommand(Action<object?> execute, Func<object?, bool>? canExecute = null) {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public bool CanExecute(object? parameter) {
        return canExecute == null || canExecute(parameter);
    }

    public void Execute(object? parameter) {
        execute(parameter);
    }
}
