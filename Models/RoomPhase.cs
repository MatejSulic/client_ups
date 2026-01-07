namespace AvalonClient.Models;

public enum RoomPhase
{
    None,      // nejsi v roomce
    Lobby,    // právě po JOINED
    Waiting,  // po WAIT (CREATE + čekáš)
    Setup,    // SETUP
    Game
}
