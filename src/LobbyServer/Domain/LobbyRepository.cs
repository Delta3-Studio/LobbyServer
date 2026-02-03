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
    static string MountKey(string name, int gameId) => name.Prefixed($"lobby_{gameId}");

    public EnterLobbyResponse? EnterOrCreate(IPAddress remote, EnterLobbyRequest req)
    {
        var lobbyName = req.LobbyName.Normalized();
        var lobbyKey = MountKey(lobbyName, req.GameId);
        var userName = req.Username.Normalized();
        var expiration = settings.Value.LobbyExpiration;
        var peerId = Guid.CreateVersion7();
        var now = time.GetUtcNow();

        var lobby = cache.GetOrCreate(lobbyKey, e =>
        {
            e.SetSlidingExpiration(expiration);
            return new Lobby(
                key: lobbyKey,
                name: lobbyName,
                owner: peerId,
                expiration: expiration,
                purgeTimeout: settings.Value.PurgeTimeout,
                createdAt: now,
                maxPlayers: req.MaxPlayers
            );
        });

        if (lobby is null || lobby.Ready)
            return null;

        lock (lobby.Locker)
        {
            var userNameIndex = 2;
            var nextUserName = userName;
            while (lobby.FindEntry(nextUserName) is not null)
                nextUserName = $"{userName}{userNameIndex++}";
            userName = nextUserName;

            Peer peer = new(userName, remote)
            {
                PeerId = peerId,
                LocalEndpoint = req.LocalEndpoint,
            };

            LobbyEntry entry = new(peer, req.Mode)
            {
                LastRead = now,
            };

            using var playerEntry = cache.CreateEntry(entry.Token);
            playerEntry.Value = entry;
            playerEntry.SetSlidingExpiration(expiration);
            entry = lobby.AddPeer(entry);
            return new(userName, lobbyName, entry.Mode, entry.Peer.PeerId, entry.Token, remote);
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
