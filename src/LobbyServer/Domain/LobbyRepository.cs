using System.Net;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LobbyServer;

public sealed class LobbyRepository(
    IMemoryCache cache,
    TimeProvider time,
    IOptions<AppSettings> settings
)
{
    static string MountKey(string name, int gameId) => $"lobby[{gameId}]::{name.Normalize()}";

    public Lobby? GetOrCreate(string name, int? maxPlayers = null, int gameId = 0)
    {
        var lobbyName = name.Normalized();
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

    public EnterLobbyResponse? Enter(
        Lobby lobby,
        IPAddress remote,
        string username,
        PeerMode mode,
        IPEndPoint? localEndpoint = null
    )
    {
        lock (lobby.Locker)
        {
            if (lobby.Ready) return null;

            var peerId = Guid.CreateVersion7();
            var userName = username.Normalized();
            var userNameIndex = 2;

            var nextUserName = userName;
            while (lobby.FindEntry(nextUserName) is not null)
                nextUserName = $"{userName}{userNameIndex++}";

            userName = nextUserName;

            var entryExpiration = lobby.ExpiresAt - time.GetUtcNow();
            if (entryExpiration < TimeSpan.Zero) return null;

            Peer peer = new(userName, remote)
            {
                PeerId = peerId,
                LocalEndpoint = localEndpoint,
            };

            LobbyEntry entry = new(peer, mode)
            {
                LastRead = time.GetUtcNow(),
            };

            using var playerEntry = cache.CreateEntry(entry.Token);
            playerEntry.Value = entry;

            playerEntry.SetSlidingExpiration(entryExpiration);
            entry = lobby.AddPeer(entry);
            return new(userName, lobby.Name, entry.Mode, entry.Peer.PeerId, entry.Token, remote);
        }
    }

    public IEnumerable<string> Get(int gameId)
    {
        var prefix = MountKey(string.Empty, gameId);

        return (cache as MemoryCache)?.Keys.OfType<string>()
               .Where(x => x.StartsWith(prefix))
               .Select(x => x.Split("::").LastOrDefault(string.Empty))
               .Where(x => !string.IsNullOrWhiteSpace(x))
               ?? [];
    }

    public Lobby? Find(string name, int gameId)
    {
        var key = MountKey(name.Normalized(), gameId);
        return cache.Get<Lobby>(key);
    }

    public LobbyEntry? FindEntry(Guid peerToken)
    {
        if (!cache.TryGetValue<LobbyEntry>(peerToken, out var entry) || entry is null)
            return null;

        var now = time.GetUtcNow();
        entry.LastRead = now;

        return entry;
    }

    public void Remove(Lobby lobby) => cache.Remove(lobby.Key);
}
