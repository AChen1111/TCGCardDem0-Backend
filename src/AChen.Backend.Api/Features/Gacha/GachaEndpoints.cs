using AChen.Backend.Api.Features.ContentDelivery;
using Microsoft.AspNetCore.Mvc;

namespace AChen.Backend.Api.Features.Gacha;

public static class GachaEndpoints
{
    public static IEndpointRouteBuilder MapGachaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/gacha/admin")
            .RequireAuthorization(ContentPublisherAuthentication.Policy)
            .RequireRateLimiting("content-management");
        admin.MapGet("/config", GetConfigAsync);
        admin.MapPut("/config", ImportConfigAsync)
            .WithMetadata(new RequestSizeLimitAttribute(GachaService.MaxCsvBytes));
        admin.MapGet("/cards", GetAllCardsAsync);
        admin.MapPut("/cards", ImportAllCardsAsync)
            .WithMetadata(new RequestSizeLimitAttribute(GachaService.MaxCsvBytes));
        return endpoints;
    }

    private static Task<GachaConfigResponse> GetConfigAsync(
        GachaService service,
        CancellationToken cancellationToken) =>
        service.GetAsync(cancellationToken);

    private static async Task<GachaConfigResponse> ImportConfigAsync(
        HttpContext context,
        GachaService service,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await context.Request.Body.CopyToAsync(stream, cancellationToken);
        return await service.ImportAsync(stream.ToArray(), cancellationToken);
    }

    private static Task<AllCardsConfigResponse> GetAllCardsAsync(
        GachaService service,
        CancellationToken cancellationToken) =>
        service.GetAllCardsAsync(cancellationToken);

    private static async Task<AllCardsConfigResponse> ImportAllCardsAsync(
        HttpContext context,
        GachaService service,
        CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await context.Request.Body.CopyToAsync(stream, cancellationToken);
        return await service.ImportAllCardsAsync(stream.ToArray(), cancellationToken);
    }
}
