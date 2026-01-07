using Avalonia.Threading;
using AvalonClient.Services;
using System;
using System.Collections.Generic;
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
    public OpponentDownViewModel OpponentDown { get; } // NEW

    // až budeš mít game page/vm, odkomentuj:
    // public GameViewModel Game { get; }

    // stack pro návrat na předchozí stránku po OPPONENT_UP
    private readonly Stack<object> _pageStack = new();

    public MainViewModel(ClientSession session)
    {
        _session = session;

        Lobby = new LobbyViewModel(_session);
        Setup = new SetupViewModel();
        OpponentDown = new OpponentDownViewModel();

        // Game = new GameViewModel(_session);

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
                // reset UI stavů
                OpponentDown.Hide();
                _pageStack.Clear();

                Setup.ResetUi  ();
                // Game.ResetOpponentUi();

                Lobby.OnDisconnected();
                CurrentPage = Lobby;
            });
    }

    private void OnServerLine(string line)
    {
        // =========================================================
        // 1) GLOBAL: návrat do lobby (platí všude, kill-switch)
        // =========================================================
        if (line.Equals("RETURNED_TO_LOBBY", StringComparison.Ordinal) ||
            line.Equals("OPPONENT_LEFT", StringComparison.Ordinal) ||
            line.StartsWith("LEFT ", StringComparison.Ordinal))
        {
            OpponentDown.Hide();
            _pageStack.Clear();

            Setup.ResetUi();
            // Game.ResetOpponentUi();

            CurrentPage = Lobby;

            // ať lobby zpracuje status + udělá LIST apod.
            Lobby.HandleServerLine(line);
            return;
        }

        // =========================================================
        // 2) GLOBAL: opponent down => přepni na dedicated stránku
        // =========================================================
        if (line.Equals("OPPONENT_DOWN", StringComparison.Ordinal))
        {
            // ulož návratovou stránku jen když už nejsme na OpponentDown
            if (!ReferenceEquals(CurrentPage, OpponentDown))
                _pageStack.Push(CurrentPage);

            CurrentPage = OpponentDown;
            OpponentDown.Show(45); // klidně parametrizuj podle serveru
            return;
        }

        // =========================================================
        // 3) GLOBAL: opponent up => vrať se tam, kde jsi byl
        // =========================================================
        if (line.Equals("OPPONENT_UP", StringComparison.Ordinal))
        {
            OpponentDown.Hide();

            // vrať se na poslední page před downem
            if (_pageStack.Count > 0)
                CurrentPage = _pageStack.Pop();
            else
                CurrentPage = Lobby; // fallback (kdyby stack byl prázdnej)

            return;
        }

        // =========================================================
        // 4) Přechod do setup (zprávy ze serveru)
        // =========================================================
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

        // =========================================================
        // 5) (Až bude game) přechod do game
        // =========================================================
        // if (line.Equals("PHASE GAME", StringComparison.Ordinal) ||
        //     line.Equals("GAME", StringComparison.Ordinal))
        // {
        //     CurrentPage = Game;
        //     return;
        // }

        // =========================================================
        // 6) Routing dle aktuální stránky
        // =========================================================
        if (ReferenceEquals(CurrentPage, Lobby))
            Lobby.HandleServerLine(line);
        else if (ReferenceEquals(CurrentPage, Setup))
            Setup.HandleServerLine(line);
        // else if (ReferenceEquals(CurrentPage, Game))
        //     Game.HandleServerLine(line);
        else if (ReferenceEquals(CurrentPage, OpponentDown))
        {
            // většinou nic; OpponentDown řeší jen DOWN/UP/RETURNED_TO_LOBBY
            // ale kdybys chtěl, můžeš sem logovat extra info
        }
        else
            Lobby.HandleServerLine(line);
    }
}
