using System.IdentityModel.Tokens.Jwt;
using AChen.Backend.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AChen.Backend.Api.Features.Social;

public static class SocialEndpoints
{
    private const long RequestLimit = 16 * 1024;

    public static IEndpointRouteBuilder MapSocialEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var friends = endpoints.MapGroup("/api/friends")
            .RequireAuthorization()
            .RequireRateLimiting("player")
            .AddEndpointFilter(NoStore);

        friends.MapGet("/", ListFriendsAsync);
        friends.MapGet("/search", SearchPlayersAsync);
        friends.MapPost("/requests", CreateRequestAsync)
            .WithMetadata(new RequestSizeLimitAttribute(RequestLimit));
        friends.MapPost("/requests/{id:guid}/accept", AcceptRequestAsync);
        friends.MapPost("/requests/{id:guid}/reject", RejectRequestAsync);

        var inbox = endpoints.MapGroup("/api")
            .RequireAuthorization()
            .RequireRateLimiting("player")
            .AddEndpointFilter(NoStore);

        inbox.MapGet("/inbox", GetInboxAsync);
        inbox.MapPost("/gifts/{id:guid}/claim", ClaimGiftAsync)
            .WithMetadata(new RequestSizeLimitAttribute(RequestLimit));
        return endpoints;
    }

    private static async Task<IResult> ListFriendsAsync(
        HttpContext context,
        SocialService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ListFriendsAsync(GetUserId(context), cancellationToken));

    private static async Task<IResult> SearchPlayersAsync(
        HttpContext context,
        [FromQuery] string? nickname,
        SocialService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.SearchPlayersAsync(GetUserId(context), nickname, cancellationToken));

    private static async Task<IResult> CreateRequestAsync(
        HttpContext context,
        CreateFriendRequestBody request,
        SocialService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.CreateRequestAsync(GetUserId(context), request, cancellationToken));

    private static async Task<IResult> AcceptRequestAsync(
        HttpContext context,
        Guid id,
        SocialService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.AcceptRequestAsync(GetUserId(context), id, cancellationToken));

    private static async Task<IResult> RejectRequestAsync(
        HttpContext context,
        Guid id,
        SocialService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.RejectRequestAsync(GetUserId(context), id, cancellationToken));

    private static async Task<IResult> GetInboxAsync(
        HttpContext context,
        SocialService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetInboxAsync(GetUserId(context), cancellationToken));

    private static async Task<IResult> ClaimGiftAsync(
        HttpContext context,
        Guid id,
        ClaimGiftRequest request,
        SocialService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.ClaimGiftAsync(GetUserId(context), id, request, cancellationToken));

    private static async ValueTask<object?> NoStore(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        return await next(context);
    }

    private static Guid GetUserId(HttpContext context)
    {
        var subject = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            throw new ApiException(
                StatusCodes.Status401Unauthorized,
                "INVALID_ACCESS_TOKEN",
                "登录状态已失效，请重新登录");
        }

        return userId;
    }
}
