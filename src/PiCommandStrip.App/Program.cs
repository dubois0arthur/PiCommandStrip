using PiCommandStrip.App.Authentication;
using PiCommandStrip.App.Configuration;
using PiCommandStrip.App.Contexts;
using PiCommandStrip.App.ForegroundWindows;
using PiCommandStrip.App.Health;
using PiCommandStrip.App.Hosting;
using PiCommandStrip.App.PcCommands;
using PiCommandStrip.App.WebSockets;

var contentRootPath = ContentRootPathResolver.Resolve(
    Directory.GetCurrentDirectory(),
    AppContext.BaseDirectory);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRootPath
});

var piCommandStripOptions = builder.Configuration
    .GetRequiredSection("PiCommandStrip")
    .Get<PiCommandStripOptions>()
    ?? throw new InvalidOperationException("PiCommandStrip configuration is required.");
var networkOptions = PiCommandStripOptionsValidator.ValidateNetwork(piCommandStripOptions.Network);

builder.WebHost.ConfigureKestrel(options =>
    options.Listen(networkOptions.ListenAddress, networkOptions.Port));

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var timeProvider = TimeProvider.System;
builder.Services.AddSingleton(timeProvider);
builder.Services.AddSingleton(new ClientAuthenticationService(
    piCommandStripOptions.Authentication.Token,
    timeProvider));
builder.Services.AddSingleton<AuthenticationAttemptLimiter>();
builder.Services.AddSingleton<HealthResponseFactory>();
builder.Services.AddPcCommands();
builder.Services.AddPiCommandStripWebSockets();
builder.Services.AddPiCommandStripContexts(piCommandStripOptions.Contexts);
builder.Services.AddForegroundWindowMonitoring();

var app = builder.Build();

app.Lifetime.ApplicationStarted.Register(() =>
    app.Logger.LogInformation(
        "Pi Command Strip dashboard available at {DashboardUrl} ({NetworkMode} mode)",
        networkOptions.DashboardUrl,
        networkOptions.LanEnabled ? "LAN" : "development"));

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.MapGet("/health", (HealthResponseFactory healthResponseFactory) =>
    Results.Ok(healthResponseFactory.Create()));
app.MapPiCommandStripWebSocket();

app.Run();
