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
                Lobby.AppendInfo($"[route] page={CurrentPage.GetType().Name} line='{line.Trim()}'");
                OnServerLine(line);
            });

        // disconnected (UI thread)
        _session.Disconnected += () =>
            Dispatcher.UIThread.Post(() =>
            {
                Setup.ResetUi();
                Game.ResetUi();
                Lobby.OnDisconnected();
                CurrentPage = Lobby;
            });
    }

    private static bool LooksLikeSetupLine(string line)
    {
        // keep this generous; better to route into Setup than to miss setup
        if (line.Equals("SETUP", StringComparison.Ordinal)) return true;

        // common variants / phase markers
        if (line.Equals("PHASE SETUP", StringComparison.Ordinal)) return true;
        if (line.Equals("PHASE_SETUP", StringComparison.Ordinal)) return true;
        if (line.StartsWith("PHASE ", StringComparison.Ordinal) && line.Contains("SETUP", StringComparison.Ordinal)) return true;

        // placing + ships confirmation
        if (line.Equals("PLACING_START", StringComparison.Ordinal)) return true;
        if (line.Equals("PLACING_STOP", StringComparison.Ordinal)) return true;
        if (line.Equals("SHIPS_OK", StringComparison.Ordinal)) return true;
        if (line.StartsWith("ERROR ", StringComparison.Ordinal) && (line.Contains("PLACING", StringComparison.Ordinal) || line.Contains("SHIPS", StringComparison.Ordinal))) return true;

        // room/join notifications may precede SETUP in some servers
        if (line.StartsWith("JOINED ", StringComparison.Ordinal)) return true;

        return false;
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

        // Robust: if we see setup-related lines, force Setup page
        if (LooksLikeSetupLine(line))
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
