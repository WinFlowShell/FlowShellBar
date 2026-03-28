using System.Windows.Input;

namespace FlowShellBar.App.Application.ViewModels;

public sealed class AsyncDelegateCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private readonly Predicate<object?>? _canExecute;

    public AsyncDelegateCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
    {
        _executeAsync = executeAsync;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter)
    {
        return _canExecute?.Invoke(parameter) ?? true;
    }

    public async void Execute(object? parameter)
    {
        await _executeAsync(parameter);
    }

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
