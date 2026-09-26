using AChen.Configuration;
using Microsoft.AspNetCore.Mvc;
namespace AChen.Backend.Api.Features.ContentDelivery;
public static class ContentEndpoints
{
    public static IEndpointRouteBuilder MapContentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var dev = endpoints.MapGroup("/api/dev").RequireAuthorization(ContentPublisherAuthentication.Policy);
        dev.MapGet("/status", async (LatestContentService service, CancellationToken ct) =>
            Results.Ok(new { project = DevelopmentProtocol.Project, protocol = DevelopmentProtocol.Version,
                targets = (await service.StatusAsync(ct)).Select(x => new { target = x.Target, contentId = x.ContentId }) }));
        dev.MapPut("/content/{platform}", async (string platform, HttpRequest request, LatestContentService service, CancellationToken ct) =>
        {
            if (platform == "Editor") return Results.BadRequest();
            return Results.Ok(await service.PublishAsync(platform, request.Body, request.Headers["X-Artifact-Sha256"].ToString(), ct));
        }).RequireRateLimiting("content-upload").WithMetadata(new RequestSizeLimitAttribute(2L * 1024 * 1024 * 1024));
        dev.MapPut("/editor-config", async (HttpRequest request, LatestContentService service, CancellationToken ct) =>
            Results.Ok(await service.PublishAsync("Editor", request.Body, request.Headers["X-Artifact-Sha256"].ToString(), ct)))
            .RequireRateLimiting("content-upload");
        endpoints.MapGet("/api/content/latest/{platform}", async (string platform, HttpResponse response, LatestContentService service, CancellationToken ct) =>
        {
            response.Headers.CacheControl = "no-store";
            return Results.Ok(await service.LatestAsync(platform, ct));
        }).RequireRateLimiting("content-manifest");
        endpoints.MapGet("/content/current/{platform}/{id}/{**path}", async (string platform, string id, string path, LatestContentService service, CancellationToken ct) =>
            Results.Stream(await service.OpenAsync(platform, id, path, ct), "application/octet-stream"));
        return endpoints;
    }
}
