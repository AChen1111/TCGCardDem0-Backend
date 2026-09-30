using System.IdentityModel.Tokens.Jwt;
using AChen.Configuration;
using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AChen.Backend.Api.Features.Activities;

public static class ActivityEndpoints
{
    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var player = endpoints.MapGroup("/api/activities").RequireAuthorization().RequireRateLimiting("player")
            .WithMetadata(new RequestSizeLimitAttribute(32 * 1024))
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                if (context.HttpContext.Request.Headers["X-Activity-Schema"] != "2")
                    throw new ApiException(409, "ACTIVITY_SCHEMA_CHANGED", "活动客户端协议需要更新到版本 2");
                return await next(context);
            });
        player.MapGet("/", async (HttpContext c, ActivityService s, CancellationToken ct) => Results.Ok(await s.ListAsync(User(c), false, ct)));
        player.MapPost("/visit", async (HttpContext c, ActivityService s, CancellationToken ct) => Results.Ok(await s.ListAsync(User(c), true, ct)));
        player.MapGet("/files/{release}/{table}.bytes", async (string release, string table, HttpContext c, ActivityConfigurationStore s, CancellationToken ct) =>
        {
            var file = await s.Download(release, table, ct);
            c.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
            c.Response.Headers.ETag = "\"" + file.Info.Sha256 + "\"";
            return Results.File(file.Path, "application/octet-stream", enableRangeProcessing: false);
        });
        player.MapPost("/{id}/claim", async (string id, ActivityClaimRequest r, HttpContext c, ActivityService s, CancellationToken ct) => Results.Ok(await s.ClaimAsync(User(c), id, r, false, ct)));
        player.MapPost("/{id}/exchange", async (string id, ActivityClaimRequest r, HttpContext c, ActivityService s, CancellationToken ct) => Results.Ok(await s.ClaimAsync(User(c), id, r, true, ct)));
        player.MapPost("/{id}/popup-shown", async (string id, ActivityPopupShownRequest r, HttpContext c, ActivityService s, CancellationToken ct) => Results.Ok(await s.PopupAsync(User(c), id, r, ct)));
        var admin = endpoints.MapGroup("/api/admin/activities").RequireAuthorization(ContentPublisherAuthentication.Policy)
            .RequireRateLimiting("activity-management").WithMetadata(new RequestSizeLimitAttribute(32 * 1024 * 1024))
            .AddEndpointFilter(async (c, next) => { c.HttpContext.Response.Headers.CacheControl = "no-store"; return await next(c); });
        admin.MapGet("/", async (ActivityConfigurationStore s, CancellationToken ct) => Results.Ok(await s.Admin(ct)));
        admin.MapGet("/history", async (ActivityConfigurationStore s, CancellationToken ct) => Results.Ok(await s.History(ct)));
        admin.MapPut("/config", async (HttpContext c, ActivityConfigurationStore s, CancellationToken ct) =>
            Results.Ok(await s.Publish(c.Request.Body, c.User.Identity!.Name ?? "content-admin", ct)));
        return endpoints;
    }
    static Guid User(HttpContext context) => Guid.Parse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
