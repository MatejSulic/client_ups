using Avalonia.Threading;
using AvalonClient.Services;
using System;
using System.ComponentModel;

namespace AvalonClient.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly ClientSession _session;

    private object _currentPage;
    public object CurrentPage
    {
        get => _currentPage;
        private set
        {
            if (ReferenceEquals(_currentPage, value)) return;
            _currentPage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentPage)));
        }
    }

    public LobbyViewModel Lobby { get; }
    public SetupViewModel Setup { get; }

    public MainViewModel(ClientSession session)
    {
        _session = session;

        Lobby = new LobbyViewModel(_session);
        Setup = new SetupViewModel();

        _currentPage = Lobby;

        // Setup -> požadavek na LEAVE (posílá MainVM, Setup nic neposílá)
        Setup.LeaveRequested += () =>
            Dispatcher.UIThread.Post(async () =>
            {
                try { await _session.SendLineAsync("LEAVE"); }
                catch { /* ignore */ }
            });

        _session.Info += s =>
            Dispatcher.UIThread.Post(() => Lobby.AppendInfo(s));

        _session.LineReceived += line =>
            Dispatcher.UIThread.Post(() =>
            {
                Lobby.AppendRx(line);
                OnServerLine(line);
            });

        _session.Disconnected += () =>
            Dispatcher.UIThread.Post(() =>
            {
                Setup.ResetOpponentUi();
                Lobby.OnDisconnected();
                CurrentPage = Lobby;
            });
    }

    private void OnServerLine(string line)
    {
        // návrat do lobby (platí všude)
        if (line.Equals("RETURNED_TO_LOBBY", StringComparison.Ordinal) ||
            line.Equals("OPPONENT_LEFT", StringComparison.Ordinal) ||
            line.StartsWith("LEFT ", StringComparison.Ordinal))
        {
            Setup.ResetOpponentUi();
            CurrentPage = Lobby;
            Lobby.HandleServerLine(line);
            return;
        }

        // přechod do setup
        if (line.Equals("SETUP", StringComparison.Ordinal) ||
            line.Equals("PHASE SETUP", StringComparison.Ordinal))
        {
            Setup.ResetOpponentUi();
            CurrentPage = Setup;

            Lobby.HandleServerLine(line);
            Setup.SetRoomBadgeFromLobby(Lobby.RoomBadge);
            Setup.SetStatus("SETUP phase.");
            return;
        }

        // opponent down/up
        if (line.Equals("OPPONENT_DOWN", StringComparison.Ordinal))
        {
            Setup.ShowOpponentDownTimeout(45);
            return;
        }

        if (line.Equals("OPPONENT_UP", StringComparison.Ordinal))
        {
            Setup.ShowOpponentUp();
            return;
        }

        // routing dle stránky (většina zpráv je lobby)
        if (ReferenceEquals(CurrentPage, Lobby))
            Lobby.HandleServerLine(line);
        else if (ReferenceEquals(CurrentPage, Setup))
            Setup.HandleServerLine(line);
        else
            Lobby.HandleServerLine(line);
    }
}
