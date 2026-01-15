using Avalonia.Threading;
using AvalonClient.Services;
using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace AvalonClient.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly ClientSession _session;
    private readonly DispatcherTimer _pingWatchdog;
    private DateTime _lastPingUtc = DateTime.MinValue;
    private bool _pingTimedOut;


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
    public ResultViewModel Result { get; }


    public MainViewModel(ClientSession session)
    {
        _session = session;

        Lobby = new LobbyViewModel(_session);
        Setup = new SetupViewModel();
        Game = new GameViewModel();
        
        _currentPage = Lobby;

        // SETUP -> LEAVE (never block UI)
        Setup.LeaveRequested += () =>
        {
            _ = Task.Run(async () =>
            {
                try { await _session.SendLineAsync("LEAVE"); }
                catch { /* ignore */ }
            });
        };

        // SETUP -> READY (send ships) (never block UI)
        Setup.ReadyShipsRequested += ships =>
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _session.SendLineAsync("PLACING_START");
                    foreach (var s in ships)
                        await _session.SendLineAsync($"PLACE {s.X} {s.Y} {s.Len} {s.Dir}");
                    await _session.SendLineAsync("PLACING_STOP");
                }
                catch (Exception ex)
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        Setup.SendFailed(ex.Message);
                        Lobby.AppendInfo("Send ships failed: " + ex);
                    });
                }
            });
        };

        // GAME -> SHOOT (never block UI)
        Game.ShootRequested += (x, y) =>
        {
            _ = Task.Run(async () =>
            {
                try { await _session.SendLineAsync($"SHOOT {x} {y}"); }
                catch { /* ignore */ }
            });
        };

        // GAME -> LEAVE (never block UI)
        Game.LeaveRequested += () =>
        {
            _ = Task.Run(async () =>
            {
                try { await _session.SendLineAsync("LEAVE"); }
                catch { /* ignore */ }
            });
        };

        // logs (UI thread)
        _session.Info += s =>
            Dispatcher.UIThread.Post(() => Lobby.AppendInfo(s));

        // line routing (UI thread)
        _session.LineReceived += line =>
            Dispatcher.UIThread.Post(() =>
            {
                Lobby.AppendRx(line);
                OnServerLine(line);
            });

        // disconnected (UI thread)
        _session.Disconnected += () =>
            Dispatcher.UIThread.Post(() =>
            {
                _pingTimedOut = false;
                _lastPingUtc = DateTime.MinValue;

                Setup.ResetUi();
                Game.ResetUi();
                Lobby.OnDisconnected();
                CurrentPage = Lobby;
            });
        Result = new ResultViewModel();

        Result.LeaveRequested += () =>
            Dispatcher.UIThread.Post(async () =>
            {
                try { await _session.SendLineAsync("LEAVE"); }
                catch { }
            });

        _pingWatchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
_pingWatchdog.Tick += (_, __) =>
{
    // hlídáme jen když to vypadá, že jsme připojeni
    if (!Lobby.IsConnected) return;
    if (_pingTimedOut) return;

    // dokud jsme nedostali ani jeden ping, nehrotíme
    if (_lastPingUtc == DateTime.MinValue) return;

    var dt = DateTime.UtcNow - _lastPingUtc;
    if (dt.TotalSeconds >= 5)
    {
        _pingTimedOut = true;
        Lobby.AppendInfo("❌ Connection lost: no PING for 5s. Disconnecting…");
        try { _session.Disconnect(); } catch { }
    }
};
_pingWatchdog.Start();

    }


    private void EnsureSetupActive()
    {
        if (ReferenceEquals(CurrentPage, Setup)) return;

        Setup.ResetUi();
        CurrentPage = Setup;

        Setup.SetRoomBadgeFromLobby(Lobby.RoomBadge);
        Setup.SetStatus("SETUP phase.");
    }

    private void OnServerLine(string line)
    {
        line = line.Trim();

        // Reply instantly + update watchdog timestamp.
        if (line.Equals("PING", StringComparison.Ordinal))
        {
            _lastPingUtc = DateTime.UtcNow;
            _pingTimedOut = false;

            _ = Task.Run(async () =>
            {
                try { await _session.SendLineAsync("PONG"); }
                catch { /* ignore */ }
            });
            return;
        }


        // RETURN TO LOBBY (everywhere)
        if (line.Equals("RETURNED_TO_LOBBY", StringComparison.Ordinal) ||
                line.StartsWith("LEFT ", StringComparison.Ordinal) ||
                line.Equals("OPPONENT_LEFT", StringComparison.Ordinal))
        {
            Setup.ResetUi();
            Game.ResetUi();
            CurrentPage = Lobby;

            Lobby.HandleServerLine(line);
            return;
        }

        // GO TO GAME
        if (line.Equals("PLAY", StringComparison.Ordinal))
        {
            // Setup might still want to see this line
            Setup.HandleServerLine(line);


            Game.SetRoomBadge(Lobby.RoomBadge);
            CurrentPage = Game;
            Game.SetFleet(Setup.GetShips());
            Game.HandleServerLine(line);
            return;
        }
        // GO TO RESULT
        if (line.Equals("WIN", StringComparison.Ordinal) ||
            line.Equals("LOSE", StringComparison.Ordinal))
        {
            Result.SetResult(line);
            CurrentPage = Result;
            return;
        }


        // Robust: if we see setup-related lines, force Setup page
        if (line.Equals("SETUP", StringComparison.Ordinal))
        {
            EnsureSetupActive();

            // let Lobby keep its internal room badge/list in sync too
            Lobby.HandleServerLine(line);
            Setup.HandleServerLine(line);
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
