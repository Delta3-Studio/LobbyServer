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
    .AddHealthChecks();

builder.Services
    .AddSingleton(TimeProvider.System)
    .AddSingleton<LobbyRepository>()
    .AddHostedService<UdpListenerService>();

var app = builder.Build();
app.UseSwagger(o => o.RouteTemplate = "docs/{documentName}/swagger.json");
app.UseSwaggerUI(o =>
{
    o.DocumentTitle = fullName;
    o.RoutePrefix = "docs";
    o.DisplayRequestDuration();
});

app.MapHealthChecks("/health").ShortCircuit();
app.UseForwardedHeaders();

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
