using AvalonClient.Commands;
using AvalonClient.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace AvalonClient.ViewModels;

public sealed class SetupViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action? LeaveRequested;

    // Ready clicked => MainVM sends: PLACING + PLACE*5 + PLACING_STOP
    public event Action<IReadOnlyList<Ship>>? ReadyShipsRequested;

    public const int N = 10;

    private readonly int[,] _occ = new int[N, N]; // 0 empty, 1 ship
    private readonly List<Ship> _ships = new();

    private bool _sending;
    private bool _opponentReady;

    private string _roomBadge = "Room: —";
    private string _status = "SETUP: Place your ships.";

    private char _dir = 'H';

    public ObservableCollection<int> FleetLens { get; } = new();

    private int _selectedLen;
    public int SelectedLen
    {
        get => _selectedLen;
        set
        {
            if (_selectedLen == value) return;
            _selectedLen = value;
            OnChanged(nameof(SelectedLen));
            OnChanged(nameof(CanPlace));
            OnChanged(nameof(CanReady));
        }
    }

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

    // helper for MainViewModel
    public void SetStatus(string s) => Status = s;

    public bool CanPlace => !_sending && FleetLens.Count > 0 && SelectedLen > 0;
    public bool CanReady => !_sending && FleetLens.Count == 0 && _ships.Count == 5;

    public string DirText => _dir == 'H' ? "HORIZONTAL" : "VERTICAL";

    public bool OpponentReady
    {
        get => _opponentReady;
        private set
        {
            if (_opponentReady == value) return;
            _opponentReady = value;
            OnChanged(nameof(OpponentReady));
            OnChanged(nameof(OpponentReadyText));
        }
    }

    public string OpponentReadyText => OpponentReady ? "✅ Opponent ready" : "⌛ Opponent not ready";

    private readonly string[] _selfRows = Enumerable.Repeat(new string('.', N), N).ToArray();
    public IReadOnlyList<string> SelfRows => _selfRows;

    public ICommand LeaveCommand { get; }
    public ICommand ToggleDirCommand { get; }
    public ICommand ResetShipsCommand { get; }
    public ICommand ReadyCommand { get; }

    public SetupViewModel()
    {
        LeaveCommand = new AsyncCommand(() =>
        {
            LeaveRequested?.Invoke();
            Status = "Leaving…";
            return System.Threading.Tasks.Task.CompletedTask;
        });

        ToggleDirCommand = new AsyncCommand(() =>
        {
            if (_sending) return System.Threading.Tasks.Task.CompletedTask;
            _dir = (_dir == 'H') ? 'V' : 'H';
            OnChanged(nameof(DirText));
            return System.Threading.Tasks.Task.CompletedTask;
        });

        ResetShipsCommand = new AsyncCommand(() =>
        {
            if (_sending) return System.Threading.Tasks.Task.CompletedTask;
            ResetShipsLocal();
            Status = "Reset done. Place ships again.";
            return System.Threading.Tasks.Task.CompletedTask;
        });

        // IMPORTANT: snapshot CanReady BEFORE toggling _sending
        ReadyCommand = new AsyncCommand(() =>
        {
            bool can = CanReady;
            int ships = _ships.Count;
            int fleet = FleetLens.Count;
            bool sending = _sending;

            // debug info (you can remove later)
            Status = $"READY clicked. CanReady={can} ships={ships} fleetLens={fleet} sending={sending}";

            if (!can) return System.Threading.Tasks.Task.CompletedTask;

            _sending = true;
            OnChanged(nameof(CanPlace));
            OnChanged(nameof(CanReady));

            Status = "Sending ships to server…";

            // send a copy
            ReadyShipsRequested?.Invoke(_ships.ToList());

            return System.Threading.Tasks.Task.CompletedTask;
        });

        ResetUi();
    }


    public void SetRoomBadgeFromLobby(string badge)
    {
        if (!string.IsNullOrWhiteSpace(badge))
            RoomBadge = badge;
    }

    public void ResetUi()
    {
        _sending = false;
        OpponentReady = false;
        _dir = 'H';
        OnChanged(nameof(DirText));

        ResetShipsLocal();
        Status = "SETUP: Place your ships.";
    }

    /// <summary>
    /// Call this from MainViewModel when sending ships fails.
    /// Otherwise _sending stays true and one player gets stuck forever.
    /// </summary>
    public void SendFailed(string reason)
    {
        _sending = false;
        OnChanged(nameof(CanPlace));
        OnChanged(nameof(CanReady));
        Status = "❌ Send failed: " + (string.IsNullOrWhiteSpace(reason) ? "unknown error" : reason);
    }

    private void ResetShipsLocal()
    {
        Array.Clear(_occ, 0, _occ.Length);
        _ships.Clear();

        FleetLens.Clear();
        FleetLens.Add(5);
        FleetLens.Add(4);
        FleetLens.Add(3);
        FleetLens.Add(3);
        FleetLens.Add(2);

        SelectedLen = FleetLens[0];

        RebuildRows();
        OnChanged(nameof(SelfRows));
        OnChanged(nameof(CanPlace));
        OnChanged(nameof(CanReady));
    }

    public void PlaceAt(int x, int y)
    {
        if (!CanPlace) return;

        int len = SelectedLen;
        char dir = _dir;

        if (!Fits(x, y, len, dir))
        {
            Status = "❌ Out of bounds.";
            return;
        }

        if (Overlaps(x, y, len, dir))
        {
            Status = "❌ Overlap with another ship.";
            return;
        }

        for (int i = 0; i < len; i++)
        {
            int cx = x + (dir == 'H' ? i : 0);
            int cy = y + (dir == 'V' ? i : 0);
            _occ[cx, cy] = 1;
        }

        _ships.Add(new Ship(x, y, len, dir));
        FleetLens.Remove(len);

        SelectedLen = FleetLens.Count > 0 ? FleetLens[0] : 0;

        RebuildRows();
        OnChanged(nameof(SelfRows));
        OnChanged(nameof(CanReady));

        if (FleetLens.Count > 0)
            Status = $"Placed {len}{dir}. Remaining: {FleetLens.Count} ships.";
        else
            Status = "✅ All ships placed. Click READY to send.";
    }

    private bool Fits(int x, int y, int len, char dir)
    {
        if (x < 0 || y < 0 || x >= N || y >= N) return false;
        return dir == 'H'
            ? x + (len - 1) < N
            : y + (len - 1) < N;
    }

    private bool Overlaps(int x, int y, int len, char dir)
    {
        for (int i = 0; i < len; i++)
        {
            int cx = x + (dir == 'H' ? i : 0);
            int cy = y + (dir == 'V' ? i : 0);
            if (_occ[cx, cy] != 0) return true;
        }
        return false;
    }

    private void RebuildRows()
    {
        for (int y = 0; y < N; y++)
        {
            char[] row = new char[N];
            for (int x = 0; x < N; x++)
                row[x] = _occ[x, y] == 1 ? 'S' : '.';
            _selfRows[y] = new string(row);
        }
        OnChanged(nameof(SelfRows));
    }

    public void HandleServerLine(string line)
    {
        line = line.Trim();

        if (line.Equals("SHIPS_OK", StringComparison.Ordinal))
        {
            // we intentionally keep _sending=true to prevent resubmitting ships
            Status = "✅ Ships accepted. Waiting for opponent…";
            return;
        }

        if (line.Equals("OPPONENT_READY", StringComparison.Ordinal))
        {
            OpponentReady = true;
            Status = "✅ Opponent ready. Starting soon…";
            return;
        }

        // any server invalid about ships/ready -> reset fully
        if (line.StartsWith("ERROR SHIPS", StringComparison.Ordinal) ||
            line.StartsWith("ERROR READY", StringComparison.Ordinal) ||
            line.StartsWith("ERROR PLACE", StringComparison.Ordinal))
        {
            _sending = false;
            OnChanged(nameof(CanPlace));
            OnChanged(nameof(CanReady));

            Status = "❌ Invalid ships. Resetting. (" + line + ")";
            ResetShipsLocal();
            return;
        }

        if (line.Equals("RETURNED_TO_LOBBY", StringComparison.Ordinal) ||
            line.Equals("OPPONENT_LEFT", StringComparison.Ordinal))
        {
            ResetUi();
            return;
        }

        if (line.Equals("PLAY", StringComparison.Ordinal))
        {
            Status = "🎮 GAME START!";
            return;
        }
    }

    public IReadOnlyList<Ship> GetShips() => _ships.AsReadOnly();

    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
