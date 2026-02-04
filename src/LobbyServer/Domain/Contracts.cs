using System.Net;

namespace LobbyServer;

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
public sealed record EnterLobbyResponse(
    string Username,
    string LobbyName,
    PeerMode Mode,
    PeerId PeerId,
    Guid Token,
    IPAddress IP
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
