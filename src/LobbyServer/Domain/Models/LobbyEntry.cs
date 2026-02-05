namespace LobbyServer.Domain;

public sealed class LobbyEntry(Peer peer, PeerMode mode)
{
    public Peer Peer { get; } = peer;
    public PeerMode Mode { get; private set; } = mode;
    public bool Ready { get; private set; }
    public EntryToken Token { get; } = EntryToken.CreateVersion7();
    public required DateTimeOffset LastRead { get; set; }

    internal Lobby? Lobby { get; set; }

    public void ToggleReady() => Ready = Mode is PeerMode.Player && !Ready;

    public bool Owns(Lobby lobby) => Peer.Id != PeerId.Empty && lobby.Owner == Peer.Id;

    public void SetMode(PeerMode mode)
    {
        Mode = mode;
        Ready = false;
    }
}
