using System.Net;
using System.Reflection;
using LobbyServer;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
var currentVersion = (Assembly.GetEntryAssembly()?.GetName().Version ?? new Version()).ToString(3);
var currentName = builder.Environment.ApplicationName;
var fullName = $"{builder.Environment.ApplicationName} - API v{currentVersion}";
Console.Title = fullName;

builder.Configuration.AddEnvironmentVariables();
builder.Services.AddOptions<AppSettings>().BindConfiguration("");
builder.Services
    .ConfigureHttpJsonOptions(options => options.SerializerOptions.AddCustomConverters())
    .Configure<JsonOptions>(o => o.JsonSerializerOptions.AddCustomConverters())
    .AddEndpointsApiExplorer()
    .AddSwaggerGen(options =>
    {
        options.MapType<IPAddress>(() => new() { Type = "string" });
        options.MapType<IPEndPoint>(() => new() { Type = "string" });
        options.SupportNonNullableReferenceTypes();
        options.SwaggerDoc("v1", new()
        {
            Title = currentName,
            Version = currentVersion,
        });
    })
    .Configure<ForwardedHeadersOptions>(o => o.ForwardedHeaders = ForwardedHeaders.XForwardedFor)
    .AddMemoryCache()
    .AddHealthChecks();

builder.Services
    .AddSingleton(TimeProvider.System)
    .AddSingleton<LobbyRepository>()
    .AddHostedService<UdpListenerService>();

var app = builder.Build();

app.UseForwardedHeaders();
app.UseSwagger().UseSwaggerUI(o =>
{
    o.DisplayRequestDuration();
    o.DocumentTitle = fullName;
    o.RoutePrefix = "docs";
});

app.MapHealthChecks("/health").ShortCircuit();
app.MapGet("/version", () => currentVersion).ShortCircuit();

Api.MapRoutes(app);

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
