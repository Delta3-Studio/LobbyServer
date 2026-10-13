global using PeerId = System.Guid;
global using EntryToken = System.Guid;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Backdash.JsonConverters;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace LobbyServer;

public static partial class Extensions
{
    [GeneratedRegex("[^a-zA-Z0-9]")]
    private static partial Regex AlphaNumRegex();

    public static string NormalizedName(this string name) =>
        AlphaNumRegex().Replace(name.Trim().ToLowerInvariant(), "_");

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

    public static bool IsBlank([NotNullWhen(false)] this string? value) => string.IsNullOrWhiteSpace(value);

    static readonly JsonConverter[] customJsonConverters =
    [
        new JsonStringEnumConverter(),
        new JsonIPAddressConverter(),
        new JsonIPEndPointConverter(),
    ];
}

public sealed class ProblemBuilder
{
    readonly Dictionary<string, List<string>> errors = [];

    public void Check<T>(
        T value, Func<T, bool> predicate,
        string? error = null,
        [CallerArgumentExpression(nameof(value))]
        string? property = null
    )
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentException.ThrowIfNullOrWhiteSpace(property);
        if (!predicate(value)) return;
        if (error.IsBlank()) error = "invalid value.";
        property = property.Split('.')[^1];
        if (!errors.TryGetValue(property, out var list))
            errors[property] = list = [];

        list.Add(error);
    }

    public bool TryGetErrors([MaybeNullWhen(false)] out Dictionary<string, string[]> problems)
    {
        if (errors.Count is 0)
        {
            problems = null;
            return false;
        }

        problems = errors.ToDictionary(e => e.Key, e => e.Value.ToArray());
        return true;
    }

    public bool GetProblems([MaybeNullWhen(false)] out ValidationProblem result, string? title = null)
    {
        if (TryGetErrors(out var problems))
        {
            result = TypedResults.ValidationProblem(problems, title);
            return true;
        }

        result = null;
        return false;
    }
}
