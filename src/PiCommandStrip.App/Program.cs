using PiCommandStrip.App.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<HealthResponseFactory>();

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/health", (HealthResponseFactory healthResponseFactory) =>
    Results.Ok(healthResponseFactory.Create()));

app.Run();
