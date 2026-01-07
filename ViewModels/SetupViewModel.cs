using Avalonia.Threading;
using AvalonClient.Commands;
using System;
using System.ComponentModel;
using System.Windows.Input;

namespace AvalonClient.ViewModels;

public sealed class SetupViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    // Setup nic neposílá do sítě, jen požádá MainVM
    public event Action? LeaveRequested;

    private string _roomBadge = "Room: —";
    private string _status = "SETUP: (ship placement will be here)";

    public string RoomBadge
    {
        get => _roomBadge;
        set { _roomBadge = value; OnChanged(nameof(RoomBadge)); }
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnChanged(nameof(Status)); }
    }

    public ICommand LeaveCommand { get; }

    public SetupViewModel()
    {
        LeaveCommand = new AsyncCommand(() =>
        {
            LeaveRequested?.Invoke();
            Status = "Leaving… (waiting for server)";
            return System.Threading.Tasks.Task.CompletedTask;
        });
    }

    public void SetRoomBadgeFromLobby(string badge)
    {
        if (!string.IsNullOrWhiteSpace(badge))
            RoomBadge = badge;
    }

    public void SetStatus(string text) => Status = text;

    public void ResetUi()
    {
        Status = "SETUP: (ship placement will be here)";
    }

    // volitelné, kdybys později chtěl něco řešit i tady
    public void HandleServerLine(string line)
    {
        if (line.Equals("RETURNED_TO_LOBBY", StringComparison.Ordinal) ||
            line.Equals("OPPONENT_LEFT", StringComparison.Ordinal))
        {
            ResetUi();
        }
    }

    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
