namespace AvalonClient.Models;

public sealed class RoomInfo
{
    public int Id { get; init; }
    public int Players { get; init; }          // 0..2
    public string State { get; init; } = "";   // WAITING/PLAYING/...
    public string Phase { get; init; } = "";   // LOBBY/SETUP/GAME/...
    public string P1 { get; init; } = "";      // e.g. "P1=UP"
    public string P2 { get; init; } = "";      // e.g. "P2=DOWN"

    public string PlayersText => $"{Players}/2";
}
