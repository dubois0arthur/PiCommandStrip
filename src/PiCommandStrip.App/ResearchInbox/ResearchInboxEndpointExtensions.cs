using PiCommandStrip.App.Authentication;

namespace PiCommandStrip.App.ResearchInbox;

public static class ResearchInboxEndpointExtensions
{
    public static IEndpointRouteBuilder MapResearchInbox(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/research-inbox");
        group.AddEndpointFilter<ResearchInboxAuthenticationFilter>();
        group.MapGet("/", GetPageAsync);
        group.MapGet("/{id:long}", GetItemAsync);
        group.MapPatch("/{id:long}/reviewed", SetReviewedAsync);
        group.MapDelete("/{id:long}", DeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> GetPageAsync(
        long? beforeId,
        int? limit,
        IResearchInboxService service,
        CancellationToken cancellationToken)
    {
        try
        {
            var page = await service.GetPageAsync(
                beforeId,
                limit ?? SqliteResearchInboxService.DefaultPageSize,
                cancellationToken);
            return NoStore(Results.Ok(new ResearchInboxPageResponse(
                page.Items.Select(ToSummary).ToArray(),
                page.NextBeforeId,
                page.HasMore)));
        }
        catch (ResearchInboxValidationException exception)
        {
            return NoStore(Results.BadRequest(new { error = exception.Message }));
        }
    }

    private static async Task<IResult> GetItemAsync(
        long id,
        IResearchInboxService service,
        CancellationToken cancellationToken)
    {
        var item = await service.GetAsync(id, cancellationToken);
        return NoStore(item is null
            ? Results.NotFound(new { error = "Research item not found." })
            : Results.Ok(ToDetail(item)));
    }

    private static async Task<IResult> SetReviewedAsync(
        long id,
        ReviewedRequest request,
        IResearchInboxService service,
        CancellationToken cancellationToken)
    {
        var exists = await service.SetReviewedAsync(id, request.IsReviewed, cancellationToken);
        return NoStore(exists
            ? Results.NoContent()
            : Results.NotFound(new { error = "Research item not found." }));
    }

    private static async Task<IResult> DeleteAsync(
        long id,
        IResearchInboxService service,
        CancellationToken cancellationToken)
    {
        var deleted = await service.DeleteAsync(id, cancellationToken);
        return NoStore(deleted
            ? Results.NoContent()
            : Results.NotFound(new { error = "Research item not found." }));
    }

    private static ResearchInboxSummaryResponse ToSummary(ResearchItem item) => new(
        item.Id,
        item.Title,
        item.Domain,
        item.SelectedText is not null,
        item.CreatedAtUtc,
        item.SourceBrowser,
        item.IsReviewed);

    private static ResearchInboxDetailResponse ToDetail(ResearchItem item) => new(
        item.Id,
        item.Title,
        item.Url,
        item.Domain,
        item.SelectedText,
        item.CreatedAtUtc,
        item.SourceBrowser,
        item.IsReviewed);

    private static IResult NoStore(IResult result) => new NoStoreResult(result);

    private sealed record ReviewedRequest(bool IsReviewed);

    private sealed record ResearchInboxPageResponse(
        IReadOnlyList<ResearchInboxSummaryResponse> Items,
        long? NextBeforeId,
        bool HasMore);

    private sealed record ResearchInboxSummaryResponse(
        long Id,
        string Title,
        string Domain,
        bool HasSelectedText,
        DateTimeOffset CreatedAtUtc,
        string SourceBrowser,
        bool IsReviewed);

    private sealed record ResearchInboxDetailResponse(
        long Id,
        string Title,
        string Url,
        string Domain,
        string? SelectedText,
        DateTimeOffset CreatedAtUtc,
        string SourceBrowser,
        bool IsReviewed);

    private sealed class NoStoreResult(IResult inner) : IResult
    {
        public Task ExecuteAsync(HttpContext httpContext)
        {
            httpContext.Response.Headers.CacheControl = "no-store";
            httpContext.Response.Headers.XContentTypeOptions = "nosniff";
            return inner.ExecuteAsync(httpContext);
        }
    }
}

public sealed class ResearchInboxAuthenticationFilter(
    ClientAuthenticationService authenticationService,
    TimeProvider timeProvider) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        var authorization = context.HttpContext.Request.Headers.Authorization.ToString();
        const string bearerPrefix = "Bearer ";
        var token = authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
            ? authorization[bearerPrefix.Length..].Trim()
            : null;
        var status = authenticationService.Authenticate(token, timeProvider.GetUtcNow());
        if (status is not ClientAuthenticationStatus.Authenticated)
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return Results.Unauthorized();
        }

        return await next(context);
    }
}

