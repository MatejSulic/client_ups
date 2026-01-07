using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AvalonClient.Commands;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> _action;
    private bool _busy;

    public AsyncCommand(Func<Task> action) => _action = action;

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => !_busy;

    public async void Execute(object? parameter)
    {
        if (_busy) return;
        _busy = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);

        try { await _action(); }
        finally
        {
            _busy = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
