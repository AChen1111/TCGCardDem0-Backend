namespace AChen.Backend.Api.Features.GameConfig;

public static class GameConfigEndpoints
{
    public static IEndpointRouteBuilder MapGameConfigEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/game-config/bootstrap", GetBootstrapAsync)
            .RequireRateLimiting("game-config");
        return endpoints;
    }

    private static async Task<IResult> GetBootstrapAsync(
        HttpContext context,
        GameConfigService service,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var response = await service.GetPublishedAsync(cancellationToken);
        var etag = $"\"game-config-{response.Revision}\"";
        context.Response.Headers.ETag = etag;
        context.Response.Headers.CacheControl = "public, max-age=0, must-revalidate";
        context.Response.Headers["X-Game-Config-Revision"] = response.Revision.ToString();
        context.Response.Headers["X-Server-Time"] = timeProvider.GetUtcNow().ToString("O");

        if (Matches(context.Request.Headers.IfNoneMatch, etag))
        {
            return Results.StatusCode(StatusCodes.Status304NotModified);
        }

        return Results.Ok(response);
    }

    private static bool Matches(Microsoft.Extensions.Primitives.StringValues values, string etag) =>
        values.SelectMany(value => value?.Split(',') ?? [])
            .Select(value => value.Trim())
            .Any(value => value == "*" || string.Equals(value, etag, StringComparison.Ordinal));
}
