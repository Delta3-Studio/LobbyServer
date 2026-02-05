using System.Net;
using System.Text.Json.Serialization;

namespace LobbyServer;

public enum PeerMode : byte
{
    Player,
    Spectator,
}

public sealed class Peer(string username, IPAddress requestAddress)
{
    public PeerId PeerId { get; init; } = PeerId.CreateVersion7();
    public string Username { get; } = username;
    public IPAddress RequestAddress { get; } = requestAddress;
    public IPEndPoint? LocalEndpoint { get; init; }
    public IPEndPoint? Endpoint { get; set; }
    public bool Ready { get; private set; }
    public bool Connected => Endpoint is not null;
    public void ToggleReady() => Ready = !Ready;
}

public sealed record LobbyEntry(Peer Peer, PeerMode Mode)
{
    public EntryToken Token { get; init; } = EntryToken.CreateVersion7();
    public required DateTimeOffset LastRead { get; set; }
    internal Lobby? Lobby { get; set; }
    public bool Owns(Lobby lobby) => Peer.PeerId != PeerId.Empty && lobby.Owner == Peer.PeerId;
}

public sealed record SpectatorMapping(PeerId Host, IList<PeerId> Watchers)
{
    public SpectatorMapping(PeerId host) : this(host, []) { }
}

[Serializable]
public sealed class Lobby(
    string key,
    string name,
    TimeSpan expiration,
    TimeSpan purgeTimeout,
    DateTimeOffset createdAt,
    int? maxPlayers = null,
    Guid? recreationKey = null
)
{
    const int DefaultMaxPlayers = 4;
    readonly List<LobbyEntry> entries = [];
    public readonly Lock Locker = new();

    [JsonIgnore]
    internal string Key { get; } = key;

    [JsonIgnore]
    internal Guid? RecreationKey { get; } = recreationKey;

    public string Name { get; } = name;
    public PeerId? Owner { get; private set; }
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public DateTimeOffset ExpiresAt => CreatedAt + expiration;
    public int MaxPlayers { get; } = maxPlayers is null or 0 ? DefaultMaxPlayers : maxPlayers.Value;

    public bool Ready =>
        Players.Count() > 1 && Players.All(p => p is { Connected: true, Ready: true })
                            && Spectators.All(s => s.Connected);

    public IEnumerable<Peer> Players
    {
        get
        {
            lock (Locker)
                return entries.Where(x => x.Mode is PeerMode.Player)
                    .Take(MaxPlayers)
                    .Select(x => x.Peer);
        }
    }

    public IEnumerable<Peer> Spectators
    {
        get
        {
            lock (Locker)
                return entries.Where(x => x.Mode is PeerMode.Spectator).Select(x => x.Peer);
        }
    }

    public IEnumerable<SpectatorMapping> SpectatorMapping
    {
        get
        {
            if (!Ready) return [];
            var players = Players.Select(x => new SpectatorMapping(x.PeerId)).ToArray();

            var playerIndex = 0;
            foreach (var spectator in Spectators)
            {
                var player = players[playerIndex++ % players.Length];
                player.Watchers.Add(spectator.PeerId);
            }

            return players;
        }
    }

    public LobbyEntry AddPeer(LobbyEntry entry)
    {
        lock (Locker)
        {
            if (Ready) return entry;
            if (Players.Count() >= MaxPlayers)
                entry = entry with { Mode = PeerMode.Spectator };

            entry.Lobby = this;
            entries.Add(entry);
            Owner ??= entry.Peer.PeerId;
            return entry;
        }
    }

    public void RemovePeer(LobbyEntry entry)
    {
        lock (Locker)
        {
            if (Ready) return;
            entries.Remove(entry);
            entry.Lobby = null;

            if (Owner == entry.Peer.PeerId)
                Owner = null;
        }
    }

    public void ChangeMode(LobbyEntry entry, PeerMode mode)
    {
        if (Ready || entry.Mode == mode) return;
        RemovePeer(entry);
        if (entry.Peer.Ready) entry.Peer.ToggleReady();
        AddPeer(entry with { Mode = mode });
    }

    public LobbyEntry? FindEntry(string username)
    {
        lock (Locker)
            return entries.Find(p =>
                p.Peer.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    public LobbyEntry? FindEntry(Guid token)
    {
        lock (Locker)
            return entries.Find(p => p.Token == token);
    }

    public bool IsEmpty()
    {
        lock (Locker)
            return entries is [];
    }

    public void Purge(DateTimeOffset now)
    {
        lock (Locker)
            entries.RemoveAll(entry => now - entry.LastRead >= purgeTimeout);
    }

    public void Clear()
    {
        lock (Locker)
            entries.Clear();
    }
}
