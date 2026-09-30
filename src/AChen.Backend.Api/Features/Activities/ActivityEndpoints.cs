using System.IdentityModel.Tokens.Jwt;
using AChen.Configuration;
using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AChen.Backend.Api.Features.Activities;

public sealed record ActivityVersionRequest(long ExpectedVersion);
public sealed record ActivityCopyRequest(string NewId);
public sealed record ActivitySaveGiftRequest(ActivityGiftDefinition Definition, long ExpectedVersion);
public static class ActivityEndpoints
{
    public static IEndpointRouteBuilder MapActivityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var player = endpoints.MapGroup("/api/activities").RequireAuthorization().RequireRateLimiting("player")
            .WithMetadata(new RequestSizeLimitAttribute(32 * 1024))
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                if (context.HttpContext.Request.Headers["X-Activity-Schema"] != "1")
                    throw new ApiException(409, "ACTIVITY_SCHEMA_CHANGED", "活动客户端协议需要更新");
                return await next(context);
            });
        player.MapGet("/", async (HttpContext c, ActivityService s, CancellationToken ct) => Results.Ok(Supported(c, await s.ListAsync(User(c), false, ct))));
        player.MapGet("/{id}", async (string id, HttpContext c, ActivityService s, CancellationToken ct) =>
            Results.Ok(Supported(c, await s.ListAsync(User(c), false, ct)).Activities.SingleOrDefault(x => x.Definition.Id == id)
                ?? throw new ApiException(404, "ACTIVITY_DISABLED", "活动已不可用")));
        player.MapPost("/visit", async (HttpContext c, ActivityService s, CancellationToken ct) => Results.Ok(Supported(c, await s.ListAsync(User(c), true, ct))));
        player.MapPost("/{id}/claim", async (string id, ActivityClaimRequest r, HttpContext c, ActivityService s, CancellationToken ct) =>
        { var response = await s.ClaimAsync(User(c), id, r, false, ct); Supported(c, response.Activities); return Results.Ok(response); });
        player.MapPost("/{id}/exchange", async (string id, ActivityClaimRequest r, HttpContext c, ActivityService s, CancellationToken ct) =>
        { var response = await s.ClaimAsync(User(c), id, r, true, ct); Supported(c, response.Activities); return Results.Ok(response); });
        player.MapPost("/{id}/popup-shown", async (string id, ActivityPopupShownRequest r, HttpContext c, ActivityService s, CancellationToken ct) => Results.Ok(Supported(c, await s.PopupAsync(User(c), id, r, ct))));
        var admin = endpoints.MapGroup("/api/admin/activities").RequireAuthorization(ContentPublisherAuthentication.Policy)
            .RequireRateLimiting("activity-management").WithMetadata(new RequestSizeLimitAttribute(512 * 1024))
            .AddEndpointFilter(async (c, next) => { c.HttpContext.Response.Headers.CacheControl = "no-store"; return await next(c); });
        admin.MapGet("/", async (ActivityService s, CancellationToken ct) => Results.Ok(await s.AdminList(ct)));
        admin.MapGet("/translations/{target}", async (string target, ActivityService s, CancellationToken ct) => Results.Ok(await s.Translations(target, ct)));
        admin.MapGet("/gifts", async (ActivityService s, CancellationToken ct) => Results.Ok(await s.Gifts(ct)));
        admin.MapPut("/gifts/{id}", async (string id, ActivitySaveGiftRequest r, ActivityService s, CancellationToken ct) =>
        {
            if (id != r.Definition.Id) throw new ApiException(422, "INVALID_GIFT", "礼包 ID 不匹配");
            return Results.Ok(await s.SaveGift(r.Definition, r.ExpectedVersion, ct));
        });
        admin.MapPost("/", async (ActivityDraftRequest r, ActivityService s, CancellationToken ct) => Results.Ok(await s.SaveDraft(r.Definition.Id, r, ct)));
        admin.MapPut("/{id}", async (string id, ActivityDraftRequest r, ActivityService s, CancellationToken ct) => Results.Ok(await s.SaveDraft(id, r, ct)));
        admin.MapPost("/{id}/publish", async (string id, ActivityVersionRequest r, HttpContext c, ActivityService s, CancellationToken ct) => Results.Ok(await s.Publish(id, r.ExpectedVersion, c.User.Identity!.Name ?? "content-admin", ct)));
        admin.MapPost("/{id}/disable", async (string id, ActivityVersionRequest r, HttpContext c, ActivityService s, CancellationToken ct) => Results.Ok(await s.Disable(id, r.ExpectedVersion, c.User.Identity!.Name ?? "content-admin", ct)));
        admin.MapPost("/{id}/copy", async (string id, ActivityCopyRequest r, ActivityService s, CancellationToken ct) => Results.Ok(await s.Copy(id, r.NewId, ct)));
        admin.MapGet("/{id}/history", async (string id, ActivityService s, CancellationToken ct) => Results.Ok(await s.History(id, ct)));
        return endpoints;
    }
    static Guid User(HttpContext context) => Guid.Parse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
    static ActivityListResponse Supported(HttpContext context, ActivityListResponse response)
    {
        var types = context.Request.Headers["X-Activity-Types"].ToString().Split(',');
        response.Activities.RemoveAll(x => !types.Contains(((int)x.Definition.Type).ToString()));
        return response;
    }
}
