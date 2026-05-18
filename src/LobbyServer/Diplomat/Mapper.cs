using LobbyServer.Domain;

namespace LobbyServer.Diplomat;

public static class Mapper
{
    public static EnterLobbyResponse MapEnterResponse(LobbyEntry entry) =>
        new(
            entry.Peer.Username,
            entry.Lobby?.Name ?? string.Empty,
            entry.Mode,
            entry.Peer.NetType,
            entry.Peer.Id,
            entry.Token,
            entry.Peer.RequestAddress
        );

    public static PeerResponse Map(LobbyEntry entry) => new(
        PeerId: entry.Peer.Id,
        Username: entry.Peer.Username,
        Mode: entry.Mode,
        NetType: entry.Peer.NetType,
        RequestAddress: entry.Peer.RequestAddress,
        LocalEndpoint: entry.Peer.LocalEndpoint,
        Endpoint: entry.Peer.Endpoint,
        Ready: entry.Ready,
        Connected: entry.Peer.Connected
    );

    public static IEnumerable<PeerResponse> Map(IEnumerable<LobbyEntry> entries) => entries.Select(Map);

    public static LobbyResponse Map(Lobby lobby)
    {
        lock (lobby.Locker)
            return new(
                Name: lobby.Name,
                CreatedAt: lobby.CreatedAt,
                ExpiresAt: lobby.ExpiresAt,
                OwnerId: lobby.Owner,
                Ready: lobby.IsReady(),
                Players: Map(lobby.GetPlayers()),
                Spectators: Map(lobby.GetSpectators()),
                SpectatorMapping: lobby.MountSpectatorMapping()
            );
    }

    public static LobbyInfo MapInfo(Lobby lobby)
    {
        lock (lobby.Locker)
        {
            var owner = lobby.FindOwner();
            return new(lobby.Name, owner?.Peer.Username, owner?.Peer.Id, lobby.CreatedAt, lobby.ExpiresAt);
        }
    }
}
