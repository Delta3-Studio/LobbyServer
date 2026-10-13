using System.Net;
using Microsoft.Extensions.Options;

namespace LobbyServer.Domain.Services;

public sealed class EndpointProvider(
    IHttpContextAccessor contextAccessor,
    IOptions<AppSettings> settings
)
{
    public IPAddress? GetClientIP()
    {
        if (contextAccessor.HttpContext is not { } context) return null;
        var headers = context.Request.Headers;
        var customHeader = settings.Value.ClientIPHeader;
        IPAddress? result;

        if (!customHeader.IsBlank()
            && headers.TryGetValue(customHeader, out var clientIPHeader)
            && IPAddress.TryParse(clientIPHeader, out var clientIP))
            result = clientIP;
        else
            result = context.Connection.RemoteIpAddress;

        return result?.MapToIPv4();
    }
}
