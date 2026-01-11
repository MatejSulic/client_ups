using AvalonClient.Commands;
using AvalonClient.Models;
using AvalonClient.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AvalonClient.ViewModels;

public sealed class LobbyViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly ClientSession _session;

    private string _host = "127.0.0.1";
    private string _portText = "5555";
    private string _nick = "Filip";
    private string _log = "";
    private string _roomsHeader = "—";

    private RoomInfo? _selectedRoom;

    private int _currentRoomId = -1;
    private int _playerNo = 0;
    private RoomPhase _roomPhase = RoomPhase.None;

    private string _roomStatus = "Not in a room.";
    private string _roomBadge = "Room: —";

    private bool _isConnected;

    public string Host { get => _host; set { _host = value; OnChanged(nameof(Host)); } }
    public string PortText { get => _portText; set { _portText = value; OnChanged(nameof(PortText)); } }
    public string Nick { get => _nick; set { _nick = value; OnChanged(nameof(Nick)); } }

    public string Log { get => _log; private set { _log = value; OnChanged(nameof(Log)); } }
    public string RoomsHeader { get => _roomsHeader; private set { _roomsHeader = value; OnChanged(nameof(RoomsHeader)); } }

    public ObservableCollection<RoomInfo> Rooms { get; } = new();

    public RoomInfo? SelectedRoom
    {
        get => _selectedRoom;
        set { _selectedRoom = value; OnChanged(nameof(SelectedRoom)); OnChanged(nameof(CanJoinSelected)); }
    }

    public string RoomStatus { get => _roomStatus; private set { _roomStatus = value; OnChanged(nameof(RoomStatus)); } }

    public string RoomBadge
    {
        get => _roomBadge;
        private set { _roomBadge = value; OnChanged(nameof(RoomBadge)); }
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set { _isConnected = value; OnChanged(nameof(IsConnected)); OnChanged(nameof(CanJoinSelected)); OnChanged(nameof(CanLeave)); }
    }

    public bool CanJoinSelected => IsConnected && SelectedRoom != null;

    public bool CanLeave =>
        IsConnected &&
        _currentRoomId != -1 &&
        (_roomPhase == RoomPhase.Waiting || _roomPhase == RoomPhase.Lobby);

    public ICommand ConnectCommand { get; }
    public ICommand DisconnectCommand { get; }
    public ICommand ListCommand { get; }
    public ICommand CreateCommand { get; }
    public ICommand JoinCommand { get; }
    public ICommand RejoinCommand { get; }
    public ICommand LeaveCommand { get; }

    public LobbyViewModel(ClientSession session)
    {
        _session = session;

        ConnectCommand = new AsyncCommand(ConnectAsync);
        DisconnectCommand = new AsyncCommand(() => { _session.Disconnect(); return Task.CompletedTask; });

        ListCommand = new AsyncCommand(() => SendSafeAsync("LIST"));
        CreateCommand = new AsyncCommand(() => SendSafeAsync("CREATE"));
        JoinCommand = new AsyncCommand(() => JoinLikeAsync("JOIN"));
        RejoinCommand = new AsyncCommand(() => JoinLikeAsync("REJOIN"));
        LeaveCommand = new AsyncCommand(() => SendSafeAsync("LEAVE"));

        ResetRoomState("Not in a room.");
        IsConnected = false;
    }

    public void AppendInfo(string line) => Log += $"{line}\n";
    public void AppendRx(string line) => Log += $"< {line}\n";

    public void OnDisconnected()
    {
        ResetRoomState("Disconnected.");
        IsConnected = false;
    }

    private async Task ConnectAsync()
    {
        if (!int.TryParse(PortText, out int port) || port <= 0 || port > 65535)
        {
            AppendInfo("Invalid port.");
            return;
        }

        if (string.IsNullOrWhiteSpace(Nick))
        {
            AppendInfo("Nick is empty.");
            return;
        }

        try
        {
            await _session.ConnectAsync(Host, port, Nick);
            IsConnected = true;
        }
        catch (Exception ex)
        {
            AppendInfo($"Connect failed: {ex.Message}");
            _session.Disconnect();
        }
    }

    private static bool ShouldAutoList(string line)
    {
        var cmd = line.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        return cmd != "LIST" && cmd != "CREATE" && cmd != "JOIN" && cmd != "REJOIN" && cmd != "LEAVE";
    }

    private async Task SendSafeAsync(string line)
    {
        try
        {
            await _session.SendLineAsync(line);

            if (ShouldAutoList(line))
                await _session.SendLineAsync("LIST");
        }
        catch
        {
            AppendInfo("Not connected.");
        }
    }

    private async Task JoinLikeAsync(string cmd)
    {
        if (!CanJoinSelected)
        {
            AppendInfo("Select a room first.");
            return;
        }

        await SendSafeAsync($"{cmd} {SelectedRoom!.Id}");
    }

    // VOLÁ MainViewModel (router)
    public void HandleServerLine(string line)
    {
        if (line.StartsWith("WELCOME", StringComparison.Ordinal))
        {
            _ = SendSafeAsync("LIST");
            return;
        }

        if (line.StartsWith("ROOMS ", StringComparison.Ordinal))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var count = (parts.Length >= 2 && int.TryParse(parts[1], out var c)) ? c : -1;

            Rooms.Clear();
            RoomsHeader = count >= 0 ? $"Server reports {count} room(s)" : "Server reports rooms";
            return;
        }

        if (line.StartsWith("ROOM ", StringComparison.Ordinal))
        {
            if (TryParseRoom(line, out var room))
                Rooms.Add(room);
            return;
        }

        if (line.StartsWith("JOINED ", StringComparison.Ordinal))
        {
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (p.Length >= 3 && int.TryParse(p[1], out var rid) && int.TryParse(p[2], out var pno))
            {
                _currentRoomId = rid;
                _playerNo = pno;
                _roomPhase = RoomPhase.Lobby;

                RoomBadge = $"Room #{rid} (P{pno})";
                RoomStatus = pno == 1 ? "In room, waiting for opponent…" : "In room, opponent found.";
                OnChanged(nameof(CanLeave));
            }
            return;
        }

        if (line.Equals("WAIT", StringComparison.Ordinal))
        {
            _roomPhase = RoomPhase.Waiting;
            RoomStatus = $"Room #{_currentRoomId}: waiting for opponent…";
            OnChanged(nameof(CanLeave));
            return;
        }

        if (line.Equals("SETUP", StringComparison.Ordinal) || line.Equals("PHASE SETUP", StringComparison.Ordinal))
        {
            _roomPhase = RoomPhase.Setup;
            OnChanged(nameof(CanLeave));
            RoomStatus = $"Room #{_currentRoomId}: SETUP.";
            return;
        }

        if (line.Equals("RETURNED_TO_LOBBY", StringComparison.Ordinal) ||
            line.StartsWith("LEFT ", StringComparison.Ordinal) ||
            line.Equals("OPPONENT_LEFT", StringComparison.Ordinal))
        {
            ResetRoomState("Back in lobby.");
            _ = SendSafeAsync("LIST");
            return;
        }
    }

    private void ResetRoomState(string status)
    {
        _currentRoomId = -1;
        _playerNo = 0;
        _roomPhase = RoomPhase.None;

        RoomBadge = "Room: —";
        RoomStatus = status;

        SelectedRoom = null;

        OnChanged(nameof(CanLeave));
        OnChanged(nameof(CanJoinSelected));
    }

    private static bool TryParseRoom(string line, out RoomInfo room)
    {
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

    private void OnChanged(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
