using LobbyServer.Domain;
using LobbyServer.Domain.Services;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Microsoft.AspNetCore.Http.TypedResults;

namespace LobbyServer.Diplomat;

public static class Routes
{
    public static void MapRoutes(WebApplication app)
    {
        app.MapGet("info", (EndpointProvider endpoint, HttpContext context) =>
            new RemoteInfo(endpoint.GetClientIP(), context.Connection.RemoteIpAddress));

        MapLobby(app);
        MapEntry(app);
    }

    static void MapLobby(WebApplication app)
    {
        var api = app.MapGroup("/{game:int}/lobby");

        api.MapPost("/",
            Results<
                Ok<EnterLobbyResponse>,
                ValidationProblem, BadRequest<string>,
                Conflict, UnprocessableEntity
            > (
                EndpointProvider endpoint,
                LobbyRepository repository,
                CreateLobbyRequest req,
                int game = 0
            ) =>
            {
                if (endpoint.GetClientIP() is not { } userIp)
                    return BadRequest("Unable to determine the client IP address.");

                ProblemBuilder builder = new();
                builder.Check(req.LobbyName, v => v.IsBlank(), "Lobby name is required.");
                builder.Check(req.LobbyName, v => v is { Length: > 40 }, "Lobby name must be 40 characters or fewer.");
                builder.Check(req.Username, v => v.IsBlank(), "Username is required.");
                builder.Check(req.CustomData, v => v?.Length > 512, "Custom data must be 512 characters or fewer.");
                builder.Check(req.MaxPlayers, v => v is < Lobby.DefaultMinPlayers or > Lobby.DefaultMaxPlayers,
                    $"Maximum players must be at least {Lobby.DefaultMinPlayers} and at most {Lobby.DefaultMaxPlayers}.");

                if (builder.GetProblems(out var problems))
                    return problems;

                if (req.ForceCreation && repository.Find(req.LobbyName, game) is { } found)
                {
                    if (req.RecreationKey is { } recreationKey && found.RecreationKey == recreationKey)
                        repository.Remove(found);
                    else
                        return Conflict();
                }

                if (repository.GetOrCreate(req.LobbyName, game, req.MaxPlayers, req.RecreationKey, req.CustomData)
                        is not { } lobby ||
                    repository.Enter(lobby, userIp, req.Username, req.Mode, req.NetType, req.Version,
                            req.LocalEndpoint)
                        is not { } entry)
                    return UnprocessableEntity();

                var response = Mapper.MapEnterResponse(entry);
                return Ok(response);
            });

        api.MapPost("/enter", Results<
            Ok<EnterLobbyResponse>,
            BadRequest<string>,
            NotFound, UnprocessableEntity
        > (
            EndpointProvider endpoint,
            LobbyRepository repository,
            EnterLobbyRequest req,
            int game = 0
        ) =>
        {
            if (endpoint.GetClientIP() is not { } userIp)
                return BadRequest("Unable to determine the client IP address.");

            if (req.LobbyName.IsBlank()) return BadRequest("Lobby name is required.");
            if (req.Username.IsBlank()) return BadRequest("Username is required.");

            if (repository.Find(req.LobbyName, game) is not { } lobby)
                return NotFound();

            if (repository.Enter(lobby, userIp, req.Username, req.Mode, req.NetType, req.Version, req.LocalEndpoint)
                is not { } entry)
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

        api.MapPut("/ready", Results<NoContent, NotFound, UnprocessableEntity> (
            LobbyRepository repository,
            [FromHeader] EntryToken token
        ) =>
        {
            if (repository.FindEntry(token) is not { } entry) return NotFound();
            if (entry.Lobby is not { } lobby) return UnprocessableEntity();
            lock (lobby.Locker) entry.ToggleReady();
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
