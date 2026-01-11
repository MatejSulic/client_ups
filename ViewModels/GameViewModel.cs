using AvalonClient.Commands;
using AvalonClient.Models;
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

    // Enemy view: '.', 'H', 'M', 'K' (K = sunk/wreck)
    private readonly char[,] _enemy = new char[N, N];

    // Self view: '.', 'S', 'H', 'M'
    private readonly char[,] _self  = new char[N, N];

    private readonly string[] _enemyRows = Enumerable.Repeat(new string('.', N), N).ToArray();
    private readonly string[] _selfRows  = Enumerable.Repeat(new string('.', N), N).ToArray();

    private int? _pendingShotX;
    private int? _pendingShotY;
    private bool _awaitingShotResult;

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

    public bool GameOver
    {
        get => _gameOver;
        private set { _gameOver = value; OnChanged(nameof(GameOver)); OnChanged(nameof(TurnText)); }
    }
    private bool _gameOver;

    public string TurnText =>
        GameOver ? "🏁 GAME OVER" :
        MyTurn ? "🟢 YOUR TURN" : "🔴 OPPONENT TURN";

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

        _pendingShotX = null;
        _pendingShotY = null;
        _awaitingShotResult = false;

        GameOver = false;
        RebuildRows();
        Status = "GAME";
        MyTurn = false;
    }

    public void SetRoomBadge(string badge)
    {
        if (!string.IsNullOrWhiteSpace(badge))
            RoomBadge = badge;
    }

    /// <summary>
    /// Call this from MainViewModel when PLAY arrives, using ships from Setup.
    /// It draws your fleet into the Self grid (S).
    /// </summary>
    public void SetFleet(IReadOnlyList<Ship> ships)
    {
        if (ships == null) return;

        // Clear only self board cells to '.' (keep enemy knowledge intact)
        for (int y = 0; y < N; y++)
        for (int x = 0; x < N; x++)
            _self[x, y] = '.';

        foreach (var s in ships)
        {
            char dir = s.Dir;
            if (dir == 'h') dir = 'H';
            if (dir == 'v') dir = 'V';

            int dx = (dir == 'H') ? 1 : 0;
            int dy = (dir == 'V') ? 1 : 0;

            for (int i = 0; i < s.Len; i++)
            {
                int cx = s.X + dx * i;
                int cy = s.Y + dy * i;
                if (cx >= 0 && cy >= 0 && cx < N && cy < N)
                    _self[cx, cy] = 'S';
            }
        }

        RebuildSelfRows();
    }

    // Called by UI on click enemy cell
    public void ShootAt(int x, int y)
    {
        if (GameOver)
        {
            Status = "Game is over.";
            return;
        }

        if (!MyTurn)
        {
            Status = "Not your turn.";
            return;
        }

        if (_awaitingShotResult)
        {
            Status = "Wait for shot result…";
            return;
        }

        if (x < 0 || y < 0 || x >= N || y >= N)
        {
            Status = "Invalid coordinates.";
            return;
        }

        // don't allow clicking already known cells
        if (_enemy[x, y] == 'H' || _enemy[x, y] == 'M' || _enemy[x, y] == 'K')
        {
            Status = "Already shot there.";
            return;
        }

        _pendingShotX = x;
        _pendingShotY = y;
        _awaitingShotResult = true;

        Status = $"Shooting {x},{y}…";
        ShootRequested?.Invoke(x, y);
    }

    public void HandleServerLine(string line)
    {
        line = line.Trim();

        // Enter game / phase info
        if (line.Equals("PLAY", StringComparison.Ordinal) ||
            line.Equals("PHASE GAME", StringComparison.Ordinal) ||
            line.Equals("PHASE PLAY", StringComparison.Ordinal))
        {
            Status = "GAME started.";
            return;
        }

        // TURN
        if (line.Equals("YOUR_TURN", StringComparison.Ordinal))
        {
            MyTurn = true;
            if (!GameOver) Status = "Your turn.";
            return;
        }

        if (line.Equals("OPP_TURN", StringComparison.Ordinal))
        {
            MyTurn = false;
            if (!GameOver) Status = "Opponent turn.";
            return;
        }

        // === New: SUNK x y x y ... (preferred) ===
        // Marks all listed enemy cells as sunk/wreck.
        if (line.StartsWith("SUNK ", StringComparison.Ordinal))
        {
            ApplySunkLine(line);
            return;
        }

        // === Shot results for OUR shot (server sends WITHOUT coords) ===
        // Fallback if you still have old server responses.
        if (line.Equals("WATER", StringComparison.Ordinal))
        {
            ApplyPendingEnemyMark('M', "Water.");
            return;
        }

        if (line.Equals("HIT", StringComparison.Ordinal))
        {
            ApplyPendingEnemyMark('H', "Hit!");
            return;
        }

        if (line.Equals("SINK", StringComparison.Ordinal))
        {
            // Without coords, mark the shot as hit.
            ApplyPendingEnemyMark('H', "Sunk!");
            return;
        }

        if (line.Equals("WIN", StringComparison.Ordinal))
        {
            ApplyPendingEnemyMarkIfAny('H');
            _awaitingShotResult = false;
            GameOver = true;
            MyTurn = false;
            Status = "🏆 You WIN!";
            return;
        }

        // === Notifications about opponent's shot (NO coords) ===
        if (line.Equals("OPP_WATER", StringComparison.Ordinal))
        {
            Status = "Opponent shot: WATER.";
            return;
        }

        if (line.Equals("OPP_HIT", StringComparison.Ordinal))
        {
            Status = "Opponent shot: HIT.";
            return;
        }

        if (line.Equals("OPP_SINK", StringComparison.Ordinal))
        {
            Status = "Opponent shot: SINK.";
            return;
        }

        if (line.Equals("LOSE", StringComparison.Ordinal))
        {
            _awaitingShotResult = false;
            GameOver = true;
            MyTurn = false;
            Status = "💀 You LOSE.";
            return;
        }

        // Optional full state (REJOIN only)
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

    private void ApplySunkLine(string line)
    {
        // Example: "SUNK 3 4 3 5 3 6"
        var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return;

        bool any = false;

        for (int i = 1; i + 1 < parts.Length; i += 2)
        {
            if (!int.TryParse(parts[i], out int x)) continue;
            if (!int.TryParse(parts[i + 1], out int y)) continue;
            if (x < 0 || y < 0 || x >= N || y >= N) continue;

            _enemy[x, y] = 'K';
            any = true;
        }

        if (any)
        {
            RebuildEnemyRows();
            Status = "Sunk! ☠️";
        }

        // Clear pending shot state even if no coords parsed (safety)
        _pendingShotX = null;
        _pendingShotY = null;
        _awaitingShotResult = false;
    }

    private void ApplyPendingEnemyMark(char mark, string status)
    {
        if (_pendingShotX is int x && _pendingShotY is int y)
        {
            _enemy[x, y] = mark;
            RebuildEnemyRows();
        }

        _pendingShotX = null;
        _pendingShotY = null;
        _awaitingShotResult = false;

        if (!GameOver) Status = status;
    }

    private void ApplyPendingEnemyMarkIfAny(char mark)
    {
        if (_pendingShotX is int x && _pendingShotY is int y)
        {
            _enemy[x, y] = mark;
            RebuildEnemyRows();
            _pendingShotX = null;
            _pendingShotY = null;
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

