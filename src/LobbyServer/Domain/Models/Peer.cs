using System.Net;

namespace LobbyServer.Domain;

public sealed class Peer(string username, IPAddress requestAddress)
{
    public PeerId Id { get; init; } = PeerId.CreateVersion7();
    public string Username { get; } = username;
    public IPAddress RequestAddress { get; } = requestAddress;
    public IPEndPoint? LocalEndpoint { get; init; }
    public IPEndPoint? Endpoint { get; set; }
    public bool Connected => Endpoint is not null;
}
