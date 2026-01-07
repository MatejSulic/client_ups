using Avalonia.Threading;
using System;
using System.ComponentModel;

namespace AvalonClient.ViewModels;

public sealed class OpponentDownViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private bool _visible;
    private int _seconds;
    private DispatcherTimer? _timer;

    private string _title = "Opponent disconnected";
    private string _status = "Waiting for opponent to reconnect…";

    public bool Visible
    {
        get => _visible;
        private set { _visible = value; OnChanged(nameof(Visible)); }
    }

    public int Seconds
    {
        get => _seconds;
        private set { _seconds = value; OnChanged(nameof(Seconds)); OnChanged(nameof(TimeoutText)); }
    }

    public string Title
    {
        get => _title;
        private set { _title = value; OnChanged(nameof(Title)); }
    }

    public string Status
    {
        get => _status;
        private set { _status = value; OnChanged(nameof(Status)); }
    }

    public string TimeoutText =>
        Seconds > 0 ? $"Auto-wait: {Seconds}s" : "Still waiting…";

    public void Show(int seconds)
    {
        StopTimer();

        Visible = true;
        Title = "Opponent disconnected";
        Status = "Waiting for opponent to reconnect…";
        Seconds = seconds;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, __) =>
        {
            if (Seconds > 0) Seconds--;
            if (Seconds <= 0)
            {
                // necháme běžet “waiting”, jen už bez odpočtu
                StopTimer();
                Seconds = 0;
                Status = "Timeout elapsed. Still waiting for server…";
            }
        };
        _timer.Start();
    }

    public void Hide()
    {
        StopTimer();
        Visible = false;
        Seconds = 0;
        Title = "Opponent disconnected";
        Status = "Waiting…";
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
