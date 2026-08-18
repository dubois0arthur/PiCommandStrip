using System.Net;
using System.Text.Encodings.Web;

namespace PiCommandStrip.App.Spotify;

public static class SpotifyOAuthEndpointExtensions
{
    public static IEndpointRouteBuilder MapSpotifyOAuth(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/spotify/auth/start", StartAuthorization);
        endpoints.MapGet("/spotify/auth/callback", CompleteAuthorizationAsync);
        return endpoints;
    }

    private static IResult StartAuthorization(
        HttpContext context,
        SpotifyConfiguration configuration,
        SpotifyOAuthCoordinator coordinator)
    {
        if (!IsLoopback(context))
        {
            return Results.NotFound();
        }

        if (!configuration.IsConfigured)
        {
            return Results.Problem(
                "Spotify is disabled or incomplete on this Windows host.",
                statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Redirect(coordinator.BeginAuthorization().AbsoluteUri);
    }

    private static async Task<IResult> CompleteAuthorizationAsync(
        HttpContext context,
        SpotifyOAuthCoordinator coordinator,
        string? code,
        string? state,
        string? error,
        CancellationToken cancellationToken)
    {
        if (!IsLoopback(context))
        {
            return Results.NotFound();
        }

        var result = await coordinator.CompleteAuthorizationAsync(
            code,
            state,
            error,
            cancellationToken);
        var title = result.Succeeded ? "Spotify connected" : "Spotify connection failed";
        var body = $"""
            <!doctype html>
            <html lang="en">
            <head><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>{HtmlEncoder.Default.Encode(title)}</title></head>
            <body style="font-family:system-ui;background:#071009;color:#e7eee8;padding:3rem">
              <main style="max-width:42rem;margin:auto"><h1>{HtmlEncoder.Default.Encode(title)}</h1><p>{HtmlEncoder.Default.Encode(result.Message)}</p></main>
            </body>
            </html>
            """;
        return Results.Content(
            body,
            "text/html; charset=utf-8",
            statusCode: result.Succeeded
                ? StatusCodes.Status200OK
                : StatusCodes.Status400BadRequest);
    }

    private static bool IsLoopback(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        return address is not null && IPAddress.IsLoopback(address);
    }
}
