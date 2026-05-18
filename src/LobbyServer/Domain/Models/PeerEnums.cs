namespace LobbyServer.Domain;

public enum PeerMode : byte
{
    Player,
    Spectator,
}

public enum PeerNetType : byte
{
    Unknown,
    Wireless,
    Wired,
}
