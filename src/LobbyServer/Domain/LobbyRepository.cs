using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LobbyServer.Domain;

public sealed class LobbyRepository(
    IMemoryCache cache,
    TimeProvider time,
    IOptions<AppSettings> settings
)
{
    static string MountKey(string name, int gameId) => $"lobby[{gameId}]::{name.NormalizedName()}";

    public Lobby? GetOrCreate(
        string name,
        int gameId,
        int? maxPlayers = null,
        Guid? recreationKey = null,
        string? customData = null
    )
    {
        var lobbyName = name.NormalizedName();
        var lobbyKey = MountKey(lobbyName, gameId);
        var expiration = settings.Value.LobbyExpiration;

        var lobby = cache.GetOrCreate(lobbyKey, e =>
        {
            e.SetSlidingExpiration(expiration);
            return new Lobby(
                key: lobbyKey,
                name: lobbyName,
                expiration: expiration,
                purgeTimeout: settings.Value.PurgeTimeout,
                createdAt: time.GetUtcNow(),
                maxPlayers: maxPlayers
            )
            {
                RecreationKey = recreationKey,
                CustomData = customData,
            };
        });

        SetUpdated(lobby);
        return lobby;
    }

    public LobbyEntry? Enter(
        Lobby lobby,
        IPAddress remote,
        string username,
        PeerMode mode,
        PeerNetType? netType,
        string? version,
        IPEndPoint? localEndpoint = null
    )
    {
        lock (lobby.Locker)
        {
            if (lobby.IsReady()) return null;

            var userName = username.NormalizedName();
            var userNameIndex = 2;

            var nextUserName = userName;
            while (lobby.FindPeer(nextUserName) is not null)
                nextUserName = $"{userName}{userNameIndex++}";

            userName = nextUserName;

            var entryExpiration = lobby.ExpirationTime;
            if (entryExpiration < TimeSpan.Zero) return null;

            Peer peer = new(userName, remote, netType ?? PeerNetType.Unknown)
            {
                LocalEndpoint = localEndpoint,
                Version = version,
            };

            LobbyEntry entry = new(peer, mode)
            {
                LastRead = time.GetUtcNow(),
            };

            using var playerEntry = cache.CreateEntry(entry.Token);
            playerEntry.Value = entry;
            playerEntry.SetSlidingExpiration(entryExpiration);
            lobby.AddPeer(entry);
            return entry;
        }
    }

    public IEnumerable<Lobby> Get(int gameId)
    {
        var prefix = MountKey(string.Empty, gameId);

        return (cache as MemoryCache)?.Keys.OfType<string>()
               .Where(key => key.StartsWith(prefix))
               .Select(FindByKey)
               .Where(l =>
               {
                   if (l is null) return false;
                   lock (l.Locker) return !l.IsReady();
               })
               .OrderByDescending(l => l?.UpdatedAt)
               .Cast<Lobby>()
               ?? [];
    }

    Lobby? FindByKey(string key)
    {
        if (cache.Get<Lobby>(key) is not { } lobby || Purge(lobby)) return null;
        SetUpdated(lobby);
        return lobby;
    }

    public Lobby? Find(string name, int gameId)
    {
        var key = MountKey(name.NormalizedName(), gameId);
        return FindByKey(key);
    }

    public LobbyEntry? FindEntry(EntryToken entryToken)
    {
        if (!cache.TryGetValue<LobbyEntry>(entryToken, out var entry) || entry is null)
            return null;

        var now = time.GetUtcNow();

        if (entry.Lobby is not { } lobby)
        {
            cache.Remove(entryToken);
            return null;
        }

        lock (lobby.Locker)
        {
            entry.LastRead = now;
            if (lobby.FindEntry(entry.Token) is null || FindByKey(lobby.Key) is null)
            {
                cache.Remove(entryToken);
                return null;
            }

            return entry;
        }
    }

    public void Remove(Lobby lobby)
    {
        lock (lobby.Locker)
        {
            lobby.Clear();
            cache.Remove(lobby.Key);
        }
    }

    public void Remove(LobbyEntry entry)
    {
        if (entry.Lobby is { } lobby)
            lock (lobby.Locker)
            {
                if (entry.Owns(lobby))
                {
                    Remove(lobby);
                }
                else
                {
                    lobby.RemovePeer(entry);
                    Purge(lobby);
                }
            }

        cache.Remove(entry.Token);
    }

    public bool ChangeMode(LobbyEntry entry, PeerMode mode)
    {
        if (entry.Mode == mode) return true;
        if (entry.Lobby is not { } lobby) return false;
        lock (lobby.Locker)
        {
            if (lobby.IsReady() || lobby.IsFull()) return false;
            entry.SetMode(mode);
            return !Purge(lobby);
        }
    }

    void SetUpdated(Lobby? lobby)
    {
        if (lobby is null) return;
        lock (lobby.Locker) lobby.SetUpdated(time.GetUtcNow());
    }

    bool Purge(Lobby lobby)
    {
        lock (lobby.Locker)
        {
            lobby.Purge(time.GetUtcNow());
            if (!lobby.IsEmpty()) return false;
            Remove(lobby);
            return true;
        }
    }
}
