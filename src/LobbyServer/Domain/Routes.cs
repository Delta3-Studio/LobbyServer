using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Microsoft.AspNetCore.Http.TypedResults;

namespace LobbyServer;

public static class Routes
{
    public static void MapRoutes(WebApplication app)
    {
        app.MapGet("info", object (HttpContext context, TimeProvider time) => new
        {
            Time = time.GetLocalNow(),
            ClientIP = context.GetRemoteClientIP()?.MapToIPv4(),
            ClientIPv6 = context.GetRemoteClientIP()?.MapToIPv6(),
            RemoteIP = context.Connection.RemoteIpAddress?.MapToIPv4(),
            RemoteIPv6 = context.Connection.RemoteIpAddress?.MapToIPv6(),
        });

        var api = app.MapGroup("/{game:int}/lobby");

        api.MapPost("/", Results<Ok<EnterLobbyResponse>, BadRequest, UnprocessableEntity> (
            HttpContext context, LobbyRepository repository, EnterOrCreateLobbyRequest req, int game = 0
        ) =>
        {
            if (string.IsNullOrWhiteSpace(req.LobbyName)
                || req.LobbyName.Length > 40 || req.MaxPlayers < 2
                || context.GetRemoteClientIP() is not { } userIp)
                return BadRequest();

            if (req.RecreationKey is { } recreationKey
                && repository.Find(req.LobbyName, game) is { } found
                && found.RecreationKey == recreationKey)
                repository.Remove(found);

            if (repository.GetOrCreate(req.LobbyName, game, req.MaxPlayers) is not { } lobby
                || repository.Enter(lobby, userIp, req.Username, req.Mode, req.LocalEndpoint) is not { } enterResponse)
                return UnprocessableEntity();

            return Ok(enterResponse);
        });

        api.MapPost("/create", Results<Created<Lobby>, BadRequest, Conflict, UnprocessableEntity> (
            LobbyRepository repository, CreateLobbyRequest req, int game = 0
        ) =>
        {
            if (string.IsNullOrWhiteSpace(req.LobbyName) || req.LobbyName.Length > 40 || req.MaxPlayers < 2)
                return BadRequest();

            if (repository.Find(req.LobbyName, game) is { } found)
            {
                if (req.RecreationKey is { } recreationKey && found.RecreationKey == recreationKey)
                    repository.Remove(found);
                else
                    return Conflict();
            }

            if (repository.GetOrCreate(req.LobbyName, game, req.MaxPlayers) is not { } lobby)
                return UnprocessableEntity();

            return Created($"lobby/{lobby.Name}", lobby);
        });

        api.MapPost("/enter", Results<Ok<EnterLobbyResponse>, BadRequest, NotFound, UnprocessableEntity> (
            HttpContext context, LobbyRepository repository, EnterLobbyRequest req, int game = 0
        ) =>
        {
            if (string.IsNullOrWhiteSpace(req.LobbyName) || context.GetRemoteClientIP() is not { } userIp)
                return BadRequest();

            if (repository.Find(req.LobbyName, game) is not { } lobby)
                return NotFound();

            if (repository.Enter(lobby, userIp, req.Username, req.Mode, req.LocalEndpoint) is not { } enterResponse)
                return UnprocessableEntity();

            return Ok(enterResponse);
        });

        api.MapGet("/", Ok<IEnumerable<string>> (LobbyRepository repository, int game = 0) =>
            Ok(repository.Get(game)));

        api.MapGet("/{name}", Results<Ok<Lobby>, NotFound> (
            LobbyRepository repository, string name, int game = 0
        ) =>
        {
            if (repository.Find(name, game) is not { } lobby) return NotFound();
            return Ok(lobby);
        });

        var entries = app.MapGroup("/entry");

        entries.MapGet("/", Results<Ok<LobbyEntry>, NotFound> (
            LobbyRepository repository, [FromHeader] EntryToken token
        ) =>
        {
            if (repository.FindEntry(token) is not { } entry) return NotFound();
            return Ok(entry);
        });

        entries.MapGet("/lobby", Results<Ok<Lobby>, NotFound> (
            LobbyRepository repository, [FromHeader] EntryToken token
        ) =>
        {
            if (repository.FindEntry(token) is not { } entry) return NotFound();
            return Ok(entry.Lobby);
        });

        entries.MapDelete("/", Results<NoContent, NotFound> (
            LobbyRepository repository, [FromHeader] EntryToken token
        ) =>
        {
            if (repository.FindEntry(token) is not { } entry) return NotFound();
            repository.Remove(entry);
            return NoContent();
        });

        entries.MapPut("/ready", Results<NoContent, NotFound> (
            LobbyRepository repository, [FromHeader] EntryToken token
        ) =>
        {
            if (repository.FindEntry(token) is not { } entry) return NotFound();
            if (entry.Mode is PeerMode.Player) entry.Peer.ToggleReady();
            return NoContent();
        });

        entries.MapPut("/mode/{mode}", Results<NoContent, NotFound, UnprocessableEntity> (
            LobbyRepository repository,
            [FromHeader] EntryToken token,
            [FromRoute] PeerMode mode
        ) =>
        {
            if (repository.FindEntry(token) is not { } entry) return NotFound();
            if (entry.Lobby is not { } lobby || lobby.Ready) return UnprocessableEntity();
            lobby.ChangePeerMode(entry, mode);
            return NoContent();
        });
    }
}
