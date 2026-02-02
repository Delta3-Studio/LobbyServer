using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using static Microsoft.AspNetCore.Http.TypedResults;

namespace LobbyServer;

public static class Api
{
    public static void MapRoutes(WebApplication app)
    {
        app.MapGet("info", object (HttpContext context, TimeProvider time) => new
        {
            Date = time.GetLocalNow(),
            ClientIP = context.GetRemoteClientIP(),
            RemoteIP = context.Connection.RemoteIpAddress,
            RemoteIPv4 = context.Connection.RemoteIpAddress?.MapToIPv4(),
        });

        app.MapPost("lobby", Results<Ok<EnterLobbyResponse>, BadRequest, Conflict, UnprocessableEntity> (
            HttpContext context, LobbyRepository repository, EnterLobbyRequest req
        ) =>
        {
            if (string.IsNullOrWhiteSpace(req.LobbyName)
                || req.LobbyName.Length > 40
                || context.GetRemoteClientIP() is not { } userIp)
                return BadRequest();

            if (repository.EnterOrCreate(userIp, req) is not { } lobbyResponse)
                return UnprocessableEntity();

            return Ok(lobbyResponse);
        });

        app.MapGet("lobby/{name}",
            Results<Ok<Lobby>, NotFound, UnauthorizedHttpResult> (
                LobbyRepository repository, TimeProvider time,
                [FromHeader] Guid? token,
                string name
            ) =>
            {
                if (repository.FindLobby(name) is not { } lobby)
                    return NotFound();

                if (token is not null)
                {
                    if (repository.FindEntry(token.Value) is null) return NotFound();
                    if (lobby.FindEntry(token.Value) is null) return Unauthorized();
                }

                lobby.Purge(time.GetUtcNow());
                if (lobby.IsEmpty())
                    repository.Remove(lobby);

                return Ok(lobby);
            });

        app.MapDelete("lobby/{name}",
            Results<NoContent, NotFound, BadRequest, UnprocessableEntity, UnauthorizedHttpResult> (
                LobbyRepository repository, [FromHeader] Guid token, string name
            ) =>
            {
                if (string.IsNullOrWhiteSpace(name)) return BadRequest();
                if (repository.FindEntry(token) is null || repository.FindLobby(name) is not { } lobby)
                    return NotFound();
                if (lobby.FindEntry(token) is not { } entry) return Unauthorized();

                if (lobby.Ready) return UnprocessableEntity();

                lock (lobby.Locker)
                {
                    lobby.RemovePeer(entry);

                    if (lobby.IsEmpty())
                        repository.Remove(lobby);
                }

                return NoContent();
            });

        app.MapPut("lobby/{name}",
            Results<NoContent, NotFound, BadRequest, UnprocessableEntity, UnauthorizedHttpResult> (
                LobbyRepository repository,
                [FromHeader] Guid token,
                string name
            ) =>
            {
                if (string.IsNullOrWhiteSpace(name))
                    return BadRequest();

                if (repository.FindEntry(token) is null || repository.FindLobby(name) is not { } lobby)
                    return NotFound();

                if (lobby.FindEntry(token) is not { } entry)
                    return Unauthorized();

                if (lobby.Ready)
                    return UnprocessableEntity();

                if (entry.Mode is PeerMode.Player)
                    lock (lobby.Locker)
                        entry.Peer.ToggleReady();

                return NoContent();
            });

        app.MapPut("lobby/{name}/mode/{mode}",
            Results<NoContent, NotFound, BadRequest, UnprocessableEntity, UnauthorizedHttpResult> (
                LobbyRepository repository,
                [FromHeader] Guid token,
                [FromRoute] string name,
                [FromRoute] PeerMode mode
            ) =>
            {
                if (string.IsNullOrWhiteSpace(name)) return BadRequest();
                if (repository.FindEntry(token) is null || repository.FindLobby(name) is not { } lobby)
                    return NotFound();
                if (lobby.FindEntry(token) is not { } entry) return Unauthorized();
                if (lobby.Ready) return UnprocessableEntity();

                lobby.ChangePeerMode(entry, mode);
                return NoContent();
            });
    }
}
