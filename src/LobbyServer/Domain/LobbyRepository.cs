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

    public Lobby? GetOrCreate(string name, int gameId, int? maxPlayers = null)
    {
        var lobbyName = name.NormalizedName();
        var lobbyKey = MountKey(lobbyName, gameId);
        var expiration = settings.Value.LobbyExpiration;

        return cache.GetOrCreate(lobbyKey, e =>
        {
            e.SetSlidingExpiration(expiration);
            return new Lobby(
                key: lobbyKey,
                name: lobbyName,
                expiration: expiration,
                purgeTimeout: settings.Value.PurgeTimeout,
                createdAt: time.GetUtcNow(),
                maxPlayers: maxPlayers
            );
        });
    }

    public LobbyEntry? Enter(
        Lobby lobby,
        IPAddress remote,
        string username,
        PeerMode mode,
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

            Peer peer = new(userName, remote)
            {
                LocalEndpoint = localEndpoint,
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
               .Select(cache.Get<Lobby>)
               .Where(l => l is not null && !l.IsReady())
               .OrderByDescending(l => l?.CreatedAt)
               .Cast<Lobby>()
               ?? [];
    }

    public Lobby? Find(string name, int gameId)
    {
        var key = MountKey(name.NormalizedName(), gameId);
        if (cache.Get<Lobby>(key) is not { } lobby) return null;
        Purge(lobby);
        return lobby;
    }

    public LobbyEntry? FindEntry(EntryToken entryToken)
    {
        if (!cache.TryGetValue<LobbyEntry>(entryToken, out var entry) || entry is null)
            return null;

        var now = time.GetUtcNow();
        entry.LastRead = now;

        if (entry.Lobby?.FindEntry(entry.Token) is null || cache.Get<Lobby>(entry.Lobby.Key) is not { } lobby)
        {
            cache.Remove(entryToken);
            return null;
        }

        Purge(lobby);
        return entry;
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
            Purge(lobby);
            return true;
        }
    }

    public void Purge(Lobby lobby)
    {
        lock (lobby.Locker)
        {
            lobby.Purge(time.GetUtcNow());
            if (lobby.IsEmpty()) Remove(lobby);
        }
    }
}
