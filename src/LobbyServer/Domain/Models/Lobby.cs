namespace LobbyServer.Domain;

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
    const int DefaultMinPlayers = 2;
    const int DefaultMaxPlayers = 4;

    readonly List<LobbyEntry> entries = [];
    internal readonly Lock Locker = new();

    public Guid Id { get; } = Guid.NewGuid();
    public string Key { get; } = key;
    public Guid? RecreationKey { get; } = recreationKey;
    public string Name { get; } = name;
    public PeerId? Owner { get; private set; }
    public DateTimeOffset CreatedAt { get; } = createdAt;
    public TimeSpan ExpirationTime { get; } = expiration;
    public DateTimeOffset ExpiresAt => CreatedAt + ExpirationTime;

    public int MaxPlayers { get; } = Math.Max(maxPlayers ?? DefaultMaxPlayers, DefaultMinPlayers);
    public IEnumerable<LobbyEntry> GetPlayers() => entries.Where(x => x.Mode is PeerMode.Player);
    public IEnumerable<LobbyEntry> GetSpectators() => entries.Where(x => x.Mode is PeerMode.Spectator);

    public bool IsReady() =>
        GetPlayers().Count(entry => entry is { Peer.Connected: true, Ready: true }) >= DefaultMinPlayers
        && GetSpectators().All(s => s.Peer.Connected);

    public SpectatorMapping[] MountSpectatorMapping()
    {
        if (!IsReady()) return [];
        var players = GetPlayers().Select(x => new SpectatorMapping(x.Peer.Id)).ToArray();

        var playerIndex = 0;
        foreach (var spectator in GetSpectators())
        {
            var player = players[playerIndex++ % players.Length];
            player.Watchers.Add(spectator.Peer.Id);
        }

        return players;
    }

    public void AddPeer(LobbyEntry entry)
    {
        if (entries.Contains(entry)) return;
        if (IsFull()) entry.SetMode(PeerMode.Spectator);
        entries.Add(entry);
        entry.Lobby = this;
        if (entry.Mode is PeerMode.Player)
            Owner ??= entry.Peer.Id;
    }

    public void RemovePeer(LobbyEntry entry)
    {
        entries.Remove(entry);
        entry.Lobby = null;
        if (entry.Owns(this))
            Owner = null;
    }

    public LobbyEntry? FindEntry(EntryToken token) => entries.Find(p => p.Token == token);

    public LobbyEntry? FindPeer(string username) =>
        entries.Find(p => p.Peer.Username.Equals(username, StringComparison.OrdinalIgnoreCase));

    public LobbyEntry? FindPeer(PeerId peerId) => entries.Find(p => p.Peer.Id == peerId);
    public LobbyEntry? FindOwner() => Owner is not { } ownerId ? null : FindPeer(ownerId);

    public bool IsEmpty() => entries is [];

    public bool IsFull() => GetPlayers().Count() >= MaxPlayers;

    public void Purge(DateTimeOffset now)
    {
        foreach (var entry in entries.FindAll(entry => now - entry.LastRead >= purgeTimeout))
            RemovePeer(entry);

        foreach (var entry in GetPlayers().Skip(MaxPlayers))
            entry.SetMode(PeerMode.Spectator);
    }

    public void Clear()
    {
        while (entries.Count > 0)
            RemovePeer(entries[0]);
    }
}

public sealed record SpectatorMapping(PeerId Host, IList<PeerId> Watchers)
{
    public SpectatorMapping(PeerId host) : this(host, []) { }
}
