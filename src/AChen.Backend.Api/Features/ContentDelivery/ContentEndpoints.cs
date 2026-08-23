using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace AChen.Backend.Api.Features.ContentDelivery;

public static class ContentEndpoints
{
    private const long SmallRequestLimit = 64 * 1024;

    public static IEndpointRouteBuilder MapContentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var management = endpoints.MapGroup("/api/content")
            .RequireAuthorization(ContentPublisherAuthentication.Policy)
            .RequireRateLimiting("content-management")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        management.MapPost("/releases", CreateReleaseAsync)
            .WithMetadata(new RequestSizeLimitAttribute(SmallRequestLimit));
        management.MapPut("/releases/{releaseId:guid}/artifact", UploadArtifactAsync)
            .RequireRateLimiting("content-upload");
        management.MapGet("/releases", ListReleasesAsync);
        management.MapGet("/releases/{releaseId:guid}", GetReleaseAsync);
        management.MapDelete("/releases/{releaseId:guid}", DeleteReleaseAsync);
        management.MapGet(
            "/active-releases/{channel}/{platform}/{appVersion}",
            GetActiveReleaseAsync);
        management.MapPut(
                "/active-releases/{channel}/{platform}/{appVersion}",
                SetActiveReleaseAsync)
            .WithMetadata(new RequestSizeLimitAttribute(SmallRequestLimit));
        management.MapGet("/publications", ListPublicationsAsync);

        endpoints.MapGet("/api/content/manifests/latest", GetLatestManifestAsync)
            .RequireRateLimiting("content-manifest");
        endpoints.MapMethods(
            "/content/releases/{releaseId:guid}/{**relativePath}",
            [HttpMethods.Get, HttpMethods.Head],
            DownloadFileAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateReleaseAsync(
        CreateContentReleaseRequest request,
        ContentReleaseService service,
        CancellationToken cancellationToken)
    {
        var release = await service.CreateAsync(request, cancellationToken);
        return Results.Created($"/api/content/releases/{release.Id:D}", release);
    }

    private static async Task<IResult> UploadArtifactAsync(
        Guid releaseId,
        HttpContext context,
        ContentReleaseService service,
        CancellationToken cancellationToken)
    {
        if (context.Request.ContentType is null ||
            !context.Request.ContentType.StartsWith("application/zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new ContentValidationException(new Dictionary<string, string[]>
            {
                ["Content-Type"] = ["Content-Type must be application/zip."]
            });
        }

        var artifactHash = context.Request.Headers["X-Artifact-Sha256"].ToString();
        var release = await service.UploadAsync(
            releaseId,
            context.Request.Body,
            context.Request.ContentLength,
            artifactHash,
            cancellationToken);
        return Results.Ok(release);
    }

    private static async Task<IResult> ListReleasesAsync(
        int? page,
        int? pageSize,
        string? platform,
        string? appVersion,
        string? state,
        ContentReleaseService service,
        CancellationToken cancellationToken)
    {
        page ??= 1;
        pageSize ??= 20;
        return Results.Ok(await service.ListAsync(
            page.Value,
            pageSize.Value,
            platform,
            appVersion,
            state,
            cancellationToken));
    }

    private static async Task<IResult> GetReleaseAsync(
        Guid releaseId,
        ContentReleaseService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetAsync(releaseId, cancellationToken));

    private static async Task<IResult> DeleteReleaseAsync(
        Guid releaseId,
        ContentReleaseService service,
        CancellationToken cancellationToken)
    {
        await service.DeleteAsync(releaseId, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetActiveReleaseAsync(
        string channel,
        string platform,
        string appVersion,
        ContentReleaseService service,
        CancellationToken cancellationToken)
    {
        var active = await service.GetActiveAsync(channel, platform, appVersion, cancellationToken);
        return active is null ? Results.NotFound() : Results.Ok(active);
    }

    private static async Task<IResult> SetActiveReleaseAsync(
        string channel,
        string platform,
        string appVersion,
        SetActiveContentReleaseRequest request,
        ContentReleaseService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.SetActiveAsync(
            channel,
            platform,
            appVersion,
            request,
            "api-key",
            cancellationToken));

    private static async Task<IResult> ListPublicationsAsync(
        int? page,
        int? pageSize,
        string? channel,
        string? platform,
        string? appVersion,
        ContentReleaseService service,
        CancellationToken cancellationToken)
    {
        page ??= 1;
        pageSize ??= 20;
        return Results.Ok(await service.ListPublicationsAsync(
            page.Value,
            pageSize.Value,
            channel,
            platform,
            appVersion,
            cancellationToken));
    }

    private static async Task<IResult> GetLatestManifestAsync(
        string channel,
        string platform,
        string appVersion,
        HttpContext context,
        ContentReleaseService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";
        return Results.Ok(await service.GetLatestManifestAsync(
            channel,
            platform,
            appVersion,
            cancellationToken));
    }

    private static async Task<IResult> DownloadFileAsync(
        Guid releaseId,
        string relativePath,
        HttpContext context,
        ContentReleaseService service,
        CancellationToken cancellationToken)
    {
        var download = await service.OpenFileAsync(releaseId, relativePath, cancellationToken);
        context.Response.Headers.CacheControl = "public,max-age=31536000,immutable";
        context.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        return Results.Stream(
            download.StoredFile.Stream,
            GetContentType(download.RelativePath),
            lastModified: download.StoredFile.LastModified,
            entityTag: new EntityTagHeaderValue($"\"{download.Sha256}\""),
            enableRangeProcessing: true);
    }

    private static string GetContentType(string path)
    {
        if (path.EndsWith(".hash", StringComparison.OrdinalIgnoreCase))
        {
            return "text/plain; charset=utf-8";
        }

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return "application/json; charset=utf-8";
        }

        return "application/octet-stream";
    }
}
