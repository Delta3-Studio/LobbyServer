global using PeerId = System.Guid;
global using EntryToken = System.Guid;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Backdash.JsonConverters;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LobbyServer;

public static partial class Extensions
{
    public static IPAddress? GetRemoteClientIP(this HttpContext context)
    {
        var headers = context.Request.Headers;
        IPAddress? result;

        if (headers.TryGetValue("fly-client-ip", out var clientIPHeader)
            && IPAddress.TryParse(clientIPHeader, out var clientIP))
            result = clientIP;
        else
            result = context.Connection.RemoteIpAddress;

        return result?.MapToIPv4();
    }

    public static SwaggerGenOptions MapStringType<T>(this SwaggerGenOptions options, string? example = null)
    {
        options.MapType<T>(() => new OpenApiSchema
        {
            Type = JsonSchemaType.String,
            Example = JsonValue.Create(example),
        });

        return options;
    }

    public static void AddCustomConverters(this JsonSerializerOptions options)
    {
        foreach (var converter in customJsonConverters)
            options.Converters.Add(converter);
    }

    public static string NormalizedName(this string name) => MyRegex().Replace(name.Trim().ToLower(), "_");


    [GeneratedRegex("[^a-zA-Z0-9]")]
    private static partial Regex MyRegex();

    static readonly JsonConverter[] customJsonConverters =
    [
        new JsonStringEnumConverter(),
        new JsonIPAddressConverter(),
        new JsonIPEndPointConverter(),
    ];
}
