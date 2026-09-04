using System.IdentityModel.Tokens.Jwt;
using AChen.Backend.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AChen.Backend.Api.Features.Players;

public static class PlayerEndpoints
{
    private const long ProfileRequestLimit = 16 * 1024;

    public static IEndpointRouteBuilder MapPlayerEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/player")
            .RequireAuthorization()
            .RequireRateLimiting("player")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        group.MapGet("/bootstrap", GetPlayerAsync);
        group.MapPatch("/profile", UpdateProfileAsync)
            .WithMetadata(new RequestSizeLimitAttribute(ProfileRequestLimit));
        return endpoints;
    }

    private static async Task<IResult> GetPlayerAsync(
        HttpContext context,
        PlayerService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.GetAsync(GetUserId(context), cancellationToken));

    private static async Task<IResult> UpdateProfileAsync(
        HttpContext context,
        UpdatePlayerProfileRequest request,
        PlayerService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.UpdateProfileAsync(
            GetUserId(context),
            request,
            cancellationToken));

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
