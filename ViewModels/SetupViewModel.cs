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

    private bool _timeoutVisible;
    private int _timeoutSeconds;
    private DispatcherTimer? _timer;

    private bool _opponentUpVisible;
    private string _opponentUpText = "";

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

    public bool TimeoutVisible
    {
        get => _timeoutVisible;
        private set { _timeoutVisible = value; OnChanged(nameof(TimeoutVisible)); OnChanged(nameof(TimeoutText)); }
    }

    public int TimeoutSeconds
    {
        get => _timeoutSeconds;
        private set { _timeoutSeconds = value; OnChanged(nameof(TimeoutSeconds)); OnChanged(nameof(TimeoutText)); }
    }

    public string TimeoutText => TimeoutVisible
        ? $"OPPONENT TIMEOUT. Waiting {TimeoutSeconds}s for RETURNED_TO_LOBBY…"
        : "";

    public bool OpponentUpVisible
    {
        get => _opponentUpVisible;
        private set { _opponentUpVisible = value; OnChanged(nameof(OpponentUpVisible)); }
    }

    public string OpponentUpText
    {
        get => _opponentUpText;
        private set { _opponentUpText = value; OnChanged(nameof(OpponentUpText)); }
    }

    public ICommand LeaveCommand { get; }

    public SetupViewModel()
    {
        LeaveCommand = new AsyncCommand(() =>
        {
            // jen request, síť řeší MainVM
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

    // --- OPPONENT_DOWN ---
    public void ShowOpponentDownTimeout(int seconds)
    {
        StopTimer();

        OpponentUpVisible = false;
        OpponentUpText = "";

        TimeoutVisible = true;
        TimeoutSeconds = seconds;
        Status = "Opponent disconnected.";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, __) =>
        {
            TimeoutSeconds--;
            if (TimeoutSeconds <= 0)
            {
                StopTimer();
                TimeoutSeconds = 0;
                Status = "Timeout elapsed. Still waiting for server…";
                TimeoutVisible = true;
            }
        };
        _timer.Start();
    }

    // --- OPPONENT_UP ---
    public void ShowOpponentUp()
    {
        StopTimer();
        TimeoutVisible = false;
        TimeoutSeconds = 0;

        OpponentUpVisible = true;
        OpponentUpText = "✅ Opponent reconnected.";
        Status = "Opponent is back. Continue setup.";
    }

    public void ResetOpponentUi()
    {
        StopTimer();
        TimeoutVisible = false;
        TimeoutSeconds = 0;
        OpponentUpVisible = false;
        OpponentUpText = "";
        Status = "SETUP: (ship placement will be here)";
    }

    // volitelné, kdybys později chtěl něco řešit i tady
    public void HandleServerLine(string line)
    {
        if (line.Equals("RETURNED_TO_LOBBY", StringComparison.Ordinal) ||
            line.Equals("OPPONENT_LEFT", StringComparison.Ordinal))
        {
            ResetOpponentUi();
        }
    }

    private void StopTimer()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer = null;
        }
    }

    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
