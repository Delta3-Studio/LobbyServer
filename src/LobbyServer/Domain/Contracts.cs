using System.Net;

namespace LobbyServer;

[Serializable]
public sealed record CreateLobbyRequest(
    string LobbyName,
    int GameId,
    int? MaxPlayers
);

[Serializable]
public sealed record EnterLobbyRequest(
    string LobbyName,
    int GameId,
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
    PeerToken Token,
    IPAddress IP
);

[Serializable]
public sealed record EnterOrCreateLobbyRequest(
    string LobbyName,
    int GameId,
    string Username,
    PeerMode Mode,
    IPEndPoint? LocalEndpoint,
    int? MaxPlayers
);
