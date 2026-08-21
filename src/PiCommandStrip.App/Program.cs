using PiCommandStrip.App.Authentication;
using PiCommandStrip.App.AudioMixer;
using PiCommandStrip.App.BrowserIntegration;
using PiCommandStrip.App.Configuration;
using PiCommandStrip.App.Contexts;
using PiCommandStrip.App.ForegroundWindows;
using PiCommandStrip.App.Health;
using PiCommandStrip.App.Hosting;
using PiCommandStrip.App.MediaSessions;
using PiCommandStrip.App.PcCommands;
using PiCommandStrip.App.ResearchInbox;
using PiCommandStrip.App.Spotify;
using PiCommandStrip.App.SystemTelemetry;
using PiCommandStrip.App.WebSockets;

var contentRootPath = ContentRootPathResolver.Resolve(
    Directory.GetCurrentDirectory(),
    AppContext.BaseDirectory);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = contentRootPath
});

// This single-user Windows host also needs its repository-external secrets in the
// named Lan environment. Re-adding environment/command-line providers preserves
// their normal precedence over User Secrets.
if (!builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets(
        typeof(PiCommandStripOptions).Assembly,
        optional: true);
    builder.Configuration.AddEnvironmentVariables();
    if (args.Length > 0)
    {
        builder.Configuration.AddCommandLine(args);
    }
}

var piCommandStripOptions = builder.Configuration
    .GetRequiredSection("PiCommandStrip")
    .Get<PiCommandStripOptions>()
    ?? throw new InvalidOperationException("PiCommandStrip configuration is required.");
var networkOptions = PiCommandStripOptionsValidator.ValidateNetwork(piCommandStripOptions.Network);
var commandCooldown = PiCommandStripOptionsValidator.ValidateCommandCooldown(piCommandStripOptions.Commands);
var systemTelemetryConfiguration = PiCommandStripOptionsValidator.ValidateSystemTelemetry(
    piCommandStripOptions.SystemTelemetry);
var browserIntegrationConfiguration = BrowserIntegrationConfiguration.Create(
    piCommandStripOptions.BrowserIntegration,
    networkOptions.Port);

builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(networkOptions.ListenAddress, networkOptions.Port);
    if (browserIntegrationConfiguration.Enabled)
    {
        options.ListenLocalhost(browserIntegrationConfiguration.Port);
    }
});

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
builder.Services.AddSpotifyIntegration(piCommandStripOptions.Spotify, networkOptions.Port);
builder.Services.AddPcCommands();
builder.Services.AddPiCommandStripWebSockets(commandCooldown);
builder.Services.AddBrowserIntegration(
    browserIntegrationConfiguration,
    piCommandStripOptions.BrowserIntegration);
builder.Services.AddPiCommandStripContexts(piCommandStripOptions.Contexts);
builder.Services.AddForegroundWindowMonitoring();
builder.Services.AddWindowsMediaSessionMonitoring();
builder.Services.AddWindowsAudioMixerMonitoring();
builder.Services.AddSystemTelemetryMonitoring(systemTelemetryConfiguration);
builder.Services.AddResearchInbox();

var app = builder.Build();
var spotifyConfiguration = app.Services.GetRequiredService<SpotifyConfiguration>();
if (spotifyConfiguration.Enabled && !spotifyConfiguration.IsConfigured)
{
    app.Logger.LogWarning(
        "Spotify enrichment is disabled by incomplete configuration: {SpotifyConfigurationIssue}",
        spotifyConfiguration.ConfigurationIssue);
}

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
app.MapMediaArtwork();
app.MapSpotifyOAuth();
app.MapBrowserIntegration();
app.MapResearchInbox();
app.MapPiCommandStripWebSocket();

app.Run();
