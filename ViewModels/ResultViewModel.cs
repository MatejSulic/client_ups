using AvalonClient.Commands;
using System;
using System.ComponentModel;
using System.Windows.Input;

namespace AvalonClient.ViewModels;

public sealed class ResultViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action? LeaveRequested;

    private string _title = "RESULT";
    public string Title
    {
        get => _title;
        private set { _title = value; OnChanged(nameof(Title)); }
    }

    private string _message = "";
    public string Message
    {
        get => _message;
        private set { _message = value; OnChanged(nameof(Message)); }
    }

    public ICommand LeaveCommand { get; }

    public ResultViewModel()
    {
        LeaveCommand = new AsyncCommand(() =>
        {
            LeaveRequested?.Invoke();
            return System.Threading.Tasks.Task.CompletedTask;
        });
    }

    public void SetResult(string result)
    {
        result = (result ?? "").Trim().ToUpperInvariant();

        if (result == "WIN")
        {
            Title = "🏆 YOU WIN";
            Message = "GG. Enemy fleet is now an archeological site.";
        }
        else if (result == "LOSS" || result == "LOSE")
        {
            Title = "💀 YOU LOSE";
            Message = "Unlucky. Run it back.";
        }
        else
        {
            Title = "RESULT";
            Message = result;
        }
    }

    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
