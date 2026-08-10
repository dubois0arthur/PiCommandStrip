using Microsoft.Net.Http.Headers;

namespace PiCommandStrip.App.MediaSessions;

public static class MediaArtworkEndpointExtensions
{
    public static IEndpointConventionBuilder MapMediaArtwork(this IEndpointRouteBuilder endpoints) =>
        endpoints.MapGet(
            $"{MediaArtworkCache.RoutePrefix}{{artworkId}}",
            ServeArtwork);

    private static IResult ServeArtwork(
        string artworkId,
        HttpContext context,
        IMediaArtworkCache cache)
    {
        if (!cache.TryGet(artworkId, out var artwork) || artwork is null)
        {
            return Results.NotFound();
        }

        var etag = $"\"{artwork.Id}\"";
        context.Response.Headers[HeaderNames.CacheControl] = "private, max-age=86400, immutable";
        context.Response.Headers[HeaderNames.ETag] = etag;
        context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";

        if (string.Equals(
                context.Request.Headers[HeaderNames.IfNoneMatch],
                etag,
                StringComparison.Ordinal))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Bytes(artwork.Bytes, artwork.ContentType);
    }
}
