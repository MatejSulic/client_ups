using Avalonia.Threading;
using AvalonClient.Commands;
using AvalonClient.Models;
using AvalonClient.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AvalonClient.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly ClientSession _session;

    // ---------- basic UI ----------
    private string _host = "127.0.0.1";
    private string _portText = "5555";
    private string _nick = "Filip";
    private string _log = "";
    private string _roomsHeader = "—";

    private RoomInfo? _selectedRoom;

    // ---------- room state ----------
    private int _currentRoomId = -1;
    private int _playerNo = 0;
    private RoomPhase _roomPhase = RoomPhase.None;

    private string _roomStatus = "Not in a room.";
    private string _roomBadge = "Room: —";

    private Page _page = Page.Lobby;
    private bool _isConnected;

    // ---------- properties ----------
    public string Host { get => _host; set { _host = value; OnChanged(nameof(Host)); } }
    public string PortText { get => _portText; set { _portText = value; OnChanged(nameof(PortText)); } }
    public string Nick { get => _nick; set { _nick = value; OnChanged(nameof(Nick)); } }

    public string Log { get => _log; private set { _log = value; OnChanged(nameof(Log)); } }
    public string RoomsHeader { get => _roomsHeader; private set { _roomsHeader = value; OnChanged(nameof(RoomsHeader)); } }

    public ObservableCollection<RoomInfo> Rooms { get; } = new();

    public RoomInfo? SelectedRoom
    {
        get => _selectedRoom;
        set
        {
            _selectedRoom = value;
            OnChanged(nameof(SelectedRoom));
            OnChanged(nameof(CanJoinSelected));
        }
    }

    public string RoomStatus { get => _roomStatus; private set { _roomStatus = value; OnChanged(nameof(RoomStatus)); } }
    public string RoomBadge { get => _roomBadge; private set { _roomBadge = value; OnChanged(nameof(RoomBadge)); } }

    public bool IsLobbyPage => _page == Page.Lobby;
    public bool IsSetupPage => _page == Page.Setup;

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            _isConnected = value;
            OnChanged(nameof(IsConnected));
            OnChanged(nameof(CanJoinSelected));
            OnChanged(nameof(CanLeave));
        }
    }

    public bool CanJoinSelected => IsConnected && SelectedRoom != null;

    // LEAVE má být možné, když jsi v roomce a jsi ve WAIT nebo Lobby (před setupem)
    public bool CanLeave =>
        IsConnected &&
        _currentRoomId != -1 &&
        (_roomPhase == RoomPhase.Waiting || _roomPhase == RoomPhase.Lobby);

    // ---------- commands ----------
    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ListCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand JoinCommand { get; }
    public ICommand RejoinCommand { get; }
    public ICommand LeaveCommand { get; }

    public MainViewModel(ClientSession session)
    {
        _session = session;

        ConnectCommand = new AsyncCommand(ConnectAsync);
        DisconnectCommand = new AsyncCommand(() => { _session.Disconnect(); return Task.CompletedTask; });

        // všechny tyhle přes SendSafeAsync => pošlou LIST automaticky (pokud jsme v lobby)
        ListCommand = new AsyncCommand(() => SendSafeAsync("LIST"));
        CreateCommand = new AsyncCommand(() => SendSafeAsync("CREATE"));
        JoinCommand = new AsyncCommand(() => JoinLikeAsync("JOIN"));
        RejoinCommand = new AsyncCommand(() => JoinLikeAsync("REJOIN"));
        LeaveCommand = new AsyncCommand(() => SendSafeAsync("LEAVE"));

        _session.Info += s => Dispatcher.UIThread.Post(() => Append($"{s}\n"));

        _session.LineReceived += line => Dispatcher.UIThread.Post(() =>
        {
            Append($"< {line}\n");
            HandleServerLine(line);
        });

        _session.Disconnected += () => Dispatcher.UIThread.Post(ResetForDisconnect);

        ResetRoomStateToLobby("Not in a room.");
        IsConnected = false;
    }

    // ---------- connection ----------
    private async Task ConnectAsync()
    {
        if (!int.TryParse(PortText, out int port) || port <= 0 || port > 65535)
        {
            Append("Invalid port.\n");
            return;
        }

        if (string.IsNullOrWhiteSpace(Nick))
        {
            Append("Nick is empty.\n");
            return;
        }

        try
        {
            await _session.ConnectAsync(Host, port, Nick);
            IsConnected = true; // po connectu true
        }
        catch (Exception ex)
        {
            Append($"Connect failed: {ex.Message}\n");
            _session.Disconnect();
        }
    }

    private static bool ShouldAutoList(string line)
{
    // command = první token
    var cmd = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];

    // Tyhle mění stav room/lobby a LIST řešíme až podle server zpráv
    return cmd != "LIST" &&
           cmd != "CREATE" &&
           cmd != "JOIN" &&
           cmd != "REJOIN" &&
           cmd != "LEAVE";
}

/// <summary>
/// Pošle command a případně pošle LIST (jen v lobby a jen pro vybrané commandy)
/// </summary>
private async Task SendSafeAsync(string line)
{
    try
    {
        await _session.SendLineAsync(line);

        if (IsLobbyPage && ShouldAutoList(line))
        {
            await _session.SendLineAsync("LIST");
        }
    }
    catch
    {
        Append("Not connected.\n");
    }
}

    private async Task JoinLikeAsync(string cmd)
    {
        if (!CanJoinSelected)
        {
            Append("Select a room first.\n");
            return;
        }

        await SendSafeAsync($"{cmd} {SelectedRoom!.Id}");
    }

    // ---------- SERVER HANDLER ----------
    private void HandleServerLine(string line)
    {
        // WELCOME -> auto LIST
        if (line.StartsWith("WELCOME", StringComparison.Ordinal))
        {
            _ = SendSafeAsync("LIST");
            return;
        }

        // LIST header
        if (line.StartsWith("ROOMS ", StringComparison.Ordinal))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var count = (parts.Length >= 2 && int.TryParse(parts[1], out var c)) ? c : -1;

            Rooms.Clear();
            RoomsHeader = count >= 0 ? $"Server reports {count} room(s)" : "Server reports rooms";
            return;
        }

        // LIST item
        if (line.StartsWith("ROOM ", StringComparison.Ordinal))
        {
            if (TryParseRoom(line, out var room))
                Rooms.Add(room);
            return;
        }

        // JOINED <roomId> <playerNo>
        if (line.StartsWith("JOINED ", StringComparison.Ordinal))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 3 && int.TryParse(p[1], out var rid) && int.TryParse(p[2], out var pno))
            {
                _currentRoomId = rid;
                _playerNo = pno;
                _roomPhase = RoomPhase.Lobby;

                SetRoomUi(rid, pno, pno == 1 ? "In room, waiting for opponent…" : "In room, opponent found.");
                OnChanged(nameof(CanLeave));
            }
            return;
        }

        // WAIT -> enable LEAVE
        if (line.Equals("WAIT", StringComparison.Ordinal))
        {
            _roomPhase = RoomPhase.Waiting;
            if (_currentRoomId != -1)
                RoomStatus = $"Room #{_currentRoomId}: waiting for opponent…";

            OnChanged(nameof(CanLeave));
            return;
        }

        // SETUP -> disable LEAVE, switch page
        if (line.Equals("SETUP", StringComparison.Ordinal) || line.Equals("PHASE SETUP", StringComparison.Ordinal))
        {
            _roomPhase = RoomPhase.Setup;
            OnChanged(nameof(CanLeave));

            if (_currentRoomId != -1)
                RoomStatus = $"Room #{_currentRoomId}: SETUP phase.";

            SetPage(Page.Setup);
            return;
        }

        // LEFT <id> OR RETURNED_TO_LOBBY
        if (line.StartsWith("LEFT ", StringComparison.Ordinal) || line.Equals("RETURNED_TO_LOBBY", StringComparison.Ordinal))
        {
            ResetRoomStateToLobby("Left room.");
            _ = SendSafeAsync("LIST");
            return;
        }

        // ROOM_CLOSED ...
        if (line.StartsWith("ROOM_CLOSED ", StringComparison.Ordinal))
        {
            ResetRoomStateToLobby(line);
            _ = SendSafeAsync("LIST");
            return;
        }
    }

    // ---------- resets ----------
    private void ResetRoomStateToLobby(string status)
    {
        _currentRoomId = -1;
        _playerNo = 0;
        _roomPhase = RoomPhase.None;

        RoomBadge = "Room: —";
        RoomStatus = status;

        SetPage(Page.Lobby);

        SelectedRoom = null;

        OnChanged(nameof(CanLeave));
        OnChanged(nameof(CanJoinSelected));
    }

    private void ResetForDisconnect()
    {
        ResetRoomStateToLobby("Disconnected.");
        IsConnected = false;
    }

    // ---------- parsing ----------
    private static bool TryParseRoom(string line, out RoomInfo room)
    {
        // ROOM <id> <players> <state> <phase> P1=UP P2=DOWN
        room = null!;
        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length < 7) return false;

        if (!int.TryParse(p[1], out var id)) return false;
        if (!int.TryParse(p[2], out var players)) return false;

        room = new RoomInfo
        {
            Id = id,
            Players = players,
            State = p[3],
            Phase = p[4],
            P1 = p[5],
            P2 = p[6]
        };
        return true;
    }

    // ---------- UI helpers ----------
    private void SetRoomUi(int roomId, int playerNo, string status)
    {
        RoomBadge = $"Room #{roomId} (P{playerNo})";
        RoomStatus = status;
    }

    private void SetPage(Page p)
    {
        _page = p;
        OnChanged(nameof(IsLobbyPage));
        OnChanged(nameof(IsSetupPage));
        OnChanged(nameof(CanLeave));
    }

    private void Append(string t) => Log += t;

    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
