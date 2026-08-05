using PiCommandStrip.App.ForegroundWindows;
using PiCommandStrip.App.Health;
using PiCommandStrip.App.PcCommands;
using PiCommandStrip.App.WebSockets;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<HealthResponseFactory>();
builder.Services.AddPcCommands();
builder.Services.AddPiCommandStripWebSockets();
builder.Services.AddForegroundWindowMonitoring();

var app = builder.Build();

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
