using LobbyServer.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Microsoft.AspNetCore.Http.TypedResults;

namespace LobbyServer.Diplomat;

public static class Routes
{
    public static void MapRoutes(WebApplication app)
    {
        app.MapGet("info", (HttpContext context) =>
            new RemoteInfo(context.GetRemoteClientIP(), context.Connection.RemoteIpAddress));

        MapLobby(app);
        MapEntry(app);
    }

    static void MapLobby(WebApplication app)
    {
        var api = app.MapGroup("/{game:int}/lobby");

        api.MapPost("/", Results<Ok<EnterLobbyResponse>, BadRequest, UnprocessableEntity> (
            HttpContext context,
            LobbyRepository repository,
            EnterOrCreateLobbyRequest req,
            int game = 0
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
                || repository.Enter(lobby, userIp, req.Username, req.Mode, req.LocalEndpoint) is not { } entry)
                return UnprocessableEntity();

            var response = Mapper.MapEnterResponse(entry);
            return Ok(response);
        });

        api.MapPost("/create", Results<Created<LobbyResponse>, BadRequest, Conflict, UnprocessableEntity> (
            LobbyRepository repository,
            CreateLobbyRequest req,
            int game = 0
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

            var response = Mapper.Map(lobby);
            return Created($"lobby/{response.Name}", response);
        });

        api.MapPost("/enter", Results<Ok<EnterLobbyResponse>, BadRequest, NotFound, UnprocessableEntity> (
            HttpContext context,
            LobbyRepository repository,
            EnterLobbyRequest req,
            int game = 0
        ) =>
        {
            if (string.IsNullOrWhiteSpace(req.LobbyName) || context.GetRemoteClientIP() is not { } userIp)
                return BadRequest();

            if (repository.Find(req.LobbyName, game) is not { } lobby)
                return NotFound();

            if (repository.Enter(lobby, userIp, req.Username, req.Mode, req.LocalEndpoint) is not { } entry)
                return UnprocessableEntity();

            var response = Mapper.MapEnterResponse(entry);
            return Ok(response);
        });

        api.MapGet("/", Ok<IEnumerable<LobbyInfo>> (LobbyRepository repository, int game = 0) =>
            Ok(repository.Get(game).Select(Mapper.MapInfo)));

        api.MapGet("/{name}", Results<Ok<LobbyResponse>, NotFound> (
                LobbyRepository repository, string name, int game = 0) =>
            repository.Find(name, game) is { } lobby ? Ok(Mapper.Map(lobby)) : NotFound());
    }

    static void MapEntry(WebApplication app)
    {
        var api = app.MapGroup("/entry");

        api.MapGet("/", Results<Ok<PeerResponse>, NotFound> (
                LobbyRepository repository, [FromHeader] EntryToken token) =>
            repository.FindEntry(token) is { } entry ? Ok(Mapper.Map(entry)) : NotFound());

        api.MapGet("/lobby", Results<Ok<LobbyResponse>, NotFound> (
                LobbyRepository repository, [FromHeader] EntryToken token) =>
            repository.FindEntry(token) is { Lobby: { } lobby } ? Ok(Mapper.Map(lobby)) : NotFound());

        api.MapDelete("/", Results<NoContent, NotFound> (
            LobbyRepository repository,
            [FromHeader] EntryToken token
        ) =>
        {
            if (repository.FindEntry(token) is not { } entry) return NotFound();
            repository.Remove(entry);
            return NoContent();
        });

        api.MapPut("/ready", Results<NoContent, NotFound> (
            LobbyRepository repository,
            [FromHeader] EntryToken token
        ) =>
        {
            if (repository.FindEntry(token) is not { } entry) return NotFound();
            entry.ToggleReady();
            return NoContent();
        });

        api.MapPut("/mode/{mode}", Results<NoContent, NotFound, UnprocessableEntity> (
            LobbyRepository repository,
            [FromHeader] EntryToken token,
            [FromRoute] PeerMode mode
        ) =>
        {
            if (repository.FindEntry(token) is not { } entry) return NotFound();
            if (!repository.ChangeMode(entry, mode)) return UnprocessableEntity();
            return NoContent();
        });
    }
}
