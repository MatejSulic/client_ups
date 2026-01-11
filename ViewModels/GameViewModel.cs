using AvalonClient.Commands;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;

namespace AvalonClient.ViewModels;

public sealed class GameViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public const int N = 10;

    public event Action<int, int>? ShootRequested;
    public event Action? LeaveRequested;

    private readonly char[,] _enemy = new char[N, N]; // '.', 'H', 'M'
    private readonly char[,] _self  = new char[N, N]; // '.', 'S', 'H', 'M'

    private readonly string[] _enemyRows = Enumerable.Repeat(new string('.', N), N).ToArray();
    private readonly string[] _selfRows  = Enumerable.Repeat(new string('.', N), N).ToArray();

    public string RoomBadge
    {
        get => _roomBadge;
        set { _roomBadge = value; OnChanged(nameof(RoomBadge)); }
    }
    private string _roomBadge = "Room: —";

    public string Status
    {
        get => _status;
        private set { _status = value; OnChanged(nameof(Status)); }
    }
    private string _status = "GAME";

    public bool MyTurn
    {
        get => _myTurn;
        private set { _myTurn = value; OnChanged(nameof(MyTurn)); OnChanged(nameof(TurnText)); }
    }
    private bool _myTurn;

    public string TurnText => MyTurn ? "🟢 YOUR TURN" : "🔴 OPPONENT TURN";

    public IReadOnlyList<string> EnemyRows => _enemyRows;
    public IReadOnlyList<string> SelfRows  => _selfRows;

    public ICommand LeaveCommand { get; }

    public GameViewModel()
    {
        LeaveCommand = new AsyncCommand(() =>
        {
            LeaveRequested?.Invoke();
            Status = "Leaving…";
            return System.Threading.Tasks.Task.CompletedTask;
        });

        ResetUi();
    }

    public void ResetUi()
    {
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
        {
            _enemy[x, y] = '.';
            _self[x, y]  = '.';
        }

        RebuildRows();
        Status = "GAME";
        MyTurn = false;
    }

    public void SetRoomBadge(string badge)
    {
        if (!string.IsNullOrWhiteSpace(badge))
            RoomBadge = badge;
    }

    // Called by UI on click enemy cell
    public void ShootAt(int x, int y)
    {
        if (!MyTurn)
        {
            Status = "Not your turn.";
            return;
        }

        if (x < 0 || y < 0 || x >= N || y >= N)
        {
            Status = "Invalid coordinates.";
            return;
        }

        // simple guard: don't allow clicking already known cells
        if (_enemy[x, y] == 'H' || _enemy[x, y] == 'M')
        {
            Status = "Already shot there.";
            return;
        }

        Status = $"Shooting {x},{y}…";
        ShootRequested?.Invoke(x, y);
    }

    public void HandleServerLine(string line)
    {
        line = line.Trim();

        // Enter game
        if (line.Equals("PLAY", StringComparison.Ordinal) ||
            line.Equals("PHASE GAME", StringComparison.Ordinal) ||
            line.Equals("PHASE PLAY", StringComparison.Ordinal))
        {
            Status = "GAME started.";
            return;
        }

        // TURN (these match your server code)
        if (line.Equals("YOUR_TURN", StringComparison.Ordinal))
        {
            MyTurn = true;
            Status = "Your turn.";
            return;
        }

        if (line.Equals("OPP_TURN", StringComparison.Ordinal))
        {
            MyTurn = false;
            Status = "Opponent turn.";
            return;
        }

        // Board updates (optional, but harmless)
        if (line.StartsWith("BENEMY ", StringComparison.Ordinal))
        {
            if (TryParseBoardLine(line, "BENEMY", out int y, out string row))
            {
                for (int x = 0; x < N && x < row.Length; x++)
                    _enemy[x, y] = row[x];
                RebuildEnemyRows();
            }
            return;
        }

        if (line.StartsWith("BSELF ", StringComparison.Ordinal))
        {
            if (TryParseBoardLine(line, "BSELF", out int y, out string row))
            {
                for (int x = 0; x < N && x < row.Length; x++)
                    _self[x, y] = row[x];
                RebuildSelfRows();
            }
            return;
        }
    }

    private static bool TryParseBoardLine(string line, string tag, out int y, out string row)
    {
        y = -1;
        row = "";

        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return false;
        if (!parts[0].Equals(tag, StringComparison.Ordinal)) return false;
        if (!int.TryParse(parts[1], out y)) return false;
        if (y < 0 || y >= N) return false;

        row = parts[2];
        return true;
    }

    private void RebuildRows()
    {
        RebuildEnemyRows();
        RebuildSelfRows();
    }

    private void RebuildEnemyRows()
    {
        for (int y = 0; y < N; y++)
        {
            char[] r = new char[N];
            for (int x = 0; x < N; x++) r[x] = _enemy[x, y];
            _enemyRows[y] = new string(r);
        }
        OnChanged(nameof(EnemyRows));
    }

    private void RebuildSelfRows()
    {
        for (int y = 0; y < N; y++)
        {
            char[] r = new char[N];
            for (int x = 0; x < N; x++) r[x] = _self[x, y];
            _selfRows[y] = new string(r);
        }
        OnChanged(nameof(SelfRows));
    }

    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
