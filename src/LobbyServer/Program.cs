using System.Net;
using System.Reflection;
using System.Threading.RateLimiting;
using LobbyServer;
using LobbyServer.Diplomat;
using LobbyServer.Domain;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
var currentVersion = (Assembly.GetEntryAssembly()?.GetName().Version ?? new Version()).ToString(3);
var currentName = builder.Environment.ApplicationName;
var fullName = $"{builder.Environment.ApplicationName} - API v{currentVersion}";
Console.Title = fullName;

builder.Configuration.AddEnvironmentVariables();
builder.Services.AddOptions<AppSettings>().BindConfiguration("");
builder.Services
    .ConfigureHttpJsonOptions(o => o.SerializerOptions.AddCustomConverters())
    .Configure<JsonOptions>(o => o.JsonSerializerOptions.AddCustomConverters())
    .Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor)
    .AddEndpointsApiExplorer()
    .AddSwaggerGen(options =>
    {
        options
            .MapStringType<IPAddress>("127.0.0.1")
            .MapStringType<IPEndPoint>("127.0.0.1:1234")
            .SupportNonNullableReferenceTypes();

        options.SwaggerDoc("v1", new()
        {
            Title = currentName,
            Version = currentVersion,
        });
    })
    .AddMemoryCache()
    .AddRateLimiter(ConfigureRateLimit)
    .AddHealthChecks();

builder.Services
    .AddSingleton(TimeProvider.System)
    .AddSingleton<LobbyRepository>()
    .AddHostedService<UdpListenerService>();

var app = builder.Build();

app.UseForwardedHeaders();
if (app.Environment.IsProduction())
    app.UseHttpsRedirection();

app.UseSwagger(o => o.RouteTemplate = "docs/{documentName}/swagger.json");
app.UseSwaggerUI(o =>
{
    o.DocumentTitle = fullName;
    o.RoutePrefix = "docs";
    o.DisplayRequestDuration();
});

app
    .UseRouting()
    .UseRateLimiter();

app.MapHealthChecks("/health").ShortCircuit();
Routes.MapRoutes(app);

if (app.Environment.IsDevelopment())
    _ = Task.Run(async () =>
    {
        while (true)
            if (Console.ReadKey().Key is ConsoleKey.Escape or ConsoleKey.Backspace)
            {
                await app.StopAsync();
                break;
            }
    });

await app.RunAsync();

return;

void ConfigureRateLimit(RateLimiterOptions options)
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync("Too Many Requests", cancellationToken);
    };

    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, IPAddress>(context =>
    {
        if (
            context.Connection.RemoteIpAddress is { } remoteIpAddress
            && !IPAddress.IsLoopback(remoteIpAddress)
        )
            return RateLimitPartition.GetTokenBucketLimiter(
                remoteIpAddress,
                _ => new()
                {
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    TokenLimit = 20,
                    TokensPerPeriod = 8,
                    AutoReplenishment = true,
                    ReplenishmentPeriod = TimeSpan.FromMilliseconds(500),
                });

        return RateLimitPartition.GetNoLimiter(IPAddress.Loopback);
    });
}
