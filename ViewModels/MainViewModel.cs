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
    public GameViewModel Game { get; }

    public MainViewModel(ClientSession session)
    {
        _session = session;

        Lobby = new LobbyViewModel(_session);
        Setup = new SetupViewModel();
        Game = new GameViewModel();

        _currentPage = Lobby;

        // SETUP -> LEAVE
        Setup.LeaveRequested += () =>
            Dispatcher.UIThread.Post(async () =>
            {
                try { await _session.SendLineAsync("LEAVE"); }
                catch { /* ignore */ }
            });

        // SETUP -> READY (send ships)
        Setup.ReadyShipsRequested += ships =>
            Dispatcher.UIThread.Post(async () =>
            {
                try
                {
                    await _session.SendLineAsync("PLACING");
                    foreach (var s in ships)
                        await _session.SendLineAsync($"PLACE {s.X} {s.Y} {s.Len} {s.Dir}");
                    await _session.SendLineAsync("PLACING_STOP");
                }
                catch { /* ignore */ }
            });

        // GAME -> SHOOT
        Game.ShootRequested += (x, y) =>
            Dispatcher.UIThread.Post(async () =>
            {
                try { await _session.SendLineAsync($"SHOOT {x} {y}"); }
                catch { /* ignore */ }
            });

        // GAME -> LEAVE
        Game.LeaveRequested += () =>
            Dispatcher.UIThread.Post(async () =>
            {
                try { await _session.SendLineAsync("LEAVE"); }
                catch { /* ignore */ }
            });

        // logs
        _session.Info += s =>
            Dispatcher.UIThread.Post(() => Lobby.AppendInfo(s));

        _session.LineReceived += line =>
    Dispatcher.UIThread.Post(() =>
    {
        Lobby.AppendRx(line);
        Lobby.AppendInfo($"[route] page={CurrentPage.GetType().Name} line='{line.Trim()}'");
        OnServerLine(line);
    });


        _session.Disconnected += () =>
            Dispatcher.UIThread.Post(() =>
            {
                Setup.ResetUi();
                Game.ResetUi();
                Lobby.OnDisconnected();
                CurrentPage = Lobby;
            });
    }

    private void OnServerLine(string line)
    {
        line = line.Trim();

        // RETURN TO LOBBY (everywhere)
        if (line.Equals("RETURNED_TO_LOBBY", StringComparison.Ordinal) ||
            line.Equals("OPPONENT_LEFT", StringComparison.Ordinal) ||
            line.StartsWith("LEFT ", StringComparison.Ordinal))
        {
            Setup.ResetUi();
            Game.ResetUi();
            CurrentPage = Lobby;

            Lobby.HandleServerLine(line);
            return;
        }

        // GO TO SETUP
        if (line.Equals("SETUP", StringComparison.Ordinal) ||
            line.Equals("PHASE SETUP", StringComparison.Ordinal))
        {
            Setup.ResetUi();
            CurrentPage = Setup;

            Lobby.HandleServerLine(line);
            Setup.SetRoomBadgeFromLobby(Lobby.RoomBadge);
            Setup.SetStatus("SETUP phase.");
            return;
        }

        // GO TO GAME (robust accept)
        if (line.Equals("PLAY", StringComparison.Ordinal) ||
            line.StartsWith("PLAY ", StringComparison.Ordinal) ||
            line.Equals("PHASE GAME", StringComparison.Ordinal) ||
            line.StartsWith("PHASE GAME ", StringComparison.Ordinal) ||
            line.Equals("PHASE PLAY", StringComparison.Ordinal) ||
            line.StartsWith("PHASE PLAY ", StringComparison.Ordinal))
        {
            Game.SetRoomBadge(Lobby.RoomBadge);
            CurrentPage = Game;

            Game.HandleServerLine(line); // let GameVM see PLAY too
            return;
        }

        // ROUTE message to current page VM
        if (ReferenceEquals(CurrentPage, Lobby))
            Lobby.HandleServerLine(line);
        else if (ReferenceEquals(CurrentPage, Setup))
            Setup.HandleServerLine(line);
        else if (ReferenceEquals(CurrentPage, Game))
            Game.HandleServerLine(line);
        else
            Lobby.HandleServerLine(line);
    }
}
