namespace AvalonClient.Models;

public sealed class RoomInfo
{
    public int Id { get; init; }
    public int Players { get; init; }
    public string State { get; init; } = "";
    public string Phase { get; init; } = "";
    public string P1 { get; init; } = "";
    public string P2 { get; init; } = "";

    public string PlayersText => $"{Players}/2";
}
