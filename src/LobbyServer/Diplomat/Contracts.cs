using System.Net;
using LobbyServer.Domain;

namespace LobbyServer.Diplomat;

[Serializable]
public sealed record CreateLobbyRequest(
    string LobbyName,
    int? MaxPlayers = null,
    Guid? RecreationKey = null
);

[Serializable]
public sealed record EnterLobbyRequest(
    string LobbyName,
    string Username,
    PeerMode Mode,
    IPEndPoint? LocalEndpoint
);

[Serializable]
public sealed record EnterOrCreateLobbyRequest(
    string LobbyName,
    string Username,
    PeerMode Mode,
    IPEndPoint? LocalEndpoint,
    int? MaxPlayers = null,
    Guid? RecreationKey = null
);

[Serializable]
public sealed record EnterLobbyResponse(
    string Username,
    string LobbyName,
    PeerMode Mode,
    PeerId PeerId,
    Guid Token,
    IPAddress IP
);

[Serializable]
public sealed record LobbyInfo(
    string Name,
    string? OwnerName,
    PeerId? OwnerId
);

[Serializable]
public sealed record PeerResponse(
    PeerId PeerId,
    string Username,
    PeerMode Mode,
    IPAddress RequestAddress,
    IPEndPoint? LocalEndpoint,
    IPEndPoint? Endpoint,
    bool Ready,
    bool Connected
);

[Serializable]
public sealed record LobbyResponse(
    string Name,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    PeerId? OwnerId,
    bool Ready,
    IEnumerable<PeerResponse> Players,
    IEnumerable<PeerResponse> Spectators,
    SpectatorMapping[] SpectatorMapping
);

[Serializable]
public sealed class RemoteInfo(IPAddress? client, IPAddress? remote)
{
    public IPAddress? ClientIP { get; } = client?.MapToIPv4();
    public IPAddress? ClientIPv6 { get; } = client?.MapToIPv6();
    public IPAddress? RemoteIP { get; } = remote?.MapToIPv4();
    public IPAddress? RemoteIPv6 { get; } = remote?.MapToIPv6();
}
