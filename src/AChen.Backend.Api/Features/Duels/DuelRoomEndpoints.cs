using System.IdentityModel.Tokens.Jwt;
using AChen.Backend.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AChen.Backend.Api.Features.Duels;

public static class DuelRoomEndpoints
{
    public static IEndpointRouteBuilder MapDuelRoomEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var rooms = endpoints.MapGroup("/api/duel/rooms").RequireAuthorization().RequireRateLimiting("player")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        rooms.MapGet("", (DuelRoomService service) => Results.Ok(service.List()));
        rooms.MapGet("/current", (HttpContext context, DuelRoomService service) => Results.Ok(service.Current(UserId(context))));
        rooms.MapGet("/{id:guid}/preview", (Guid id, int seat, AChen.Duel.Core.DuelZone zone, HttpContext context, DuelRoomService service) =>
            Results.Ok(service.Preview(UserId(context), id, seat, zone)));
        var replays = endpoints.MapGroup("/api/duel/replays").RequireAuthorization().RequireRateLimiting("player");
        replays.AddEndpointFilter(async (context, next) => { context.HttpContext.Response.Headers.CacheControl = "no-store"; return await next(context); });
        replays.MapGet("", (HttpContext context, DuelReplayStore store) => Results.Ok(store.List(UserId(context))));
        replays.MapGet("/{id:guid}", (Guid id, HttpContext context, DuelReplayStore store) => Results.Ok(store.Get(UserId(context), id)));
        rooms.MapPost("", (HttpContext context, DuelRoomService service) => Results.Ok(service.Create(UserId(context))));
        rooms.MapPost("/join", (JoinRoomRequest request, HttpContext context, DuelRoomService service) =>
            Results.Ok(service.Join(UserId(context), request.Code)));
        rooms.MapGet("/{id:guid}", (Guid id, HttpContext context, DuelRoomService service) =>
            Results.Ok(service.Get(UserId(context), id)));
        rooms.MapGet("/{id:guid}/socket", DuelRoomSocket.RunAsync);
        rooms.MapGet("/{id:guid}/names", (Guid id, string actionToken, string query, int offset, HttpContext context, DuelRoomService service) =>
            Results.Ok(service.QueryNames(UserId(context), id, actionToken, query, offset)));
        rooms.MapPut("/{id:guid}/deck", (Guid id, DuelDeckRequest request, HttpContext context, DuelRoomService service) =>
            Results.Ok(service.SubmitDeck(UserId(context), id, request)))
            .WithMetadata(new RequestSizeLimitAttribute(16 * 1024));
        rooms.MapPost("/{id:guid}/ready", (Guid id, ReadyRoomRequest request, HttpContext context, DuelRoomService service) =>
            Results.Ok(service.SetReady(UserId(context), id, request.Ready)));
        rooms.MapPost("/{id:guid}/return", (Guid id, HttpContext context, DuelRoomService service) =>
            Results.Ok(service.ReturnToRoom(UserId(context), id)));
        rooms.MapDelete("/{id:guid}", (Guid id, HttpContext context, DuelRoomService service) =>
        {
            service.Leave(UserId(context), id);
            return Results.NoContent();
        });
        return endpoints;
    }

    internal static Guid UserId(HttpContext context) =>
        Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var userId)
            ? userId : throw new ApiException(401, "INVALID_ACCESS_TOKEN", "登录状态已失效，请重新登录");
}

public sealed record JoinRoomRequest(string Code);
public sealed record ReadyRoomRequest(bool Ready);
