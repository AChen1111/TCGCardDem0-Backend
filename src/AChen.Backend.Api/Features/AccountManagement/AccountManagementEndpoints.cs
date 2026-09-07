using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace AChen.Backend.Api.Features.AccountManagement;

public static class AccountManagementEndpoints
{
    private const long RequestLimit = 16 * 1024;

    public static IEndpointRouteBuilder MapAccountManagementEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/accounts/admin")
            .RequireAuthorization(ContentPublisherAuthentication.Policy)
            .RequireRateLimiting("content-management")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        admin.MapGet("/gold", GetGoldAsync);
        admin.MapPost("/gold", AddGoldAsync)
            .WithMetadata(new RequestSizeLimitAttribute(RequestLimit));
        return endpoints;
    }

    private static async Task<IResult> GetGoldAsync(
        [FromQuery] string username,
        AccountManagementService service,
        CancellationToken cancellationToken)
    {
        var summary = await service.GetGoldByUsernameAsync(username, cancellationToken);
        if (summary is null)
        {
            throw new ApiException(StatusCodes.Status404NotFound, "ACCOUNT_NOT_FOUND", "未找到该账号");
        }

        return Results.Ok(summary);
    }

    private static async Task<IResult> AddGoldAsync(
        AddAccountGoldRequest request,
        AccountManagementService service,
        CancellationToken cancellationToken) =>
        Results.Ok(await service.AddGoldByUsernameAsync(request, cancellationToken));
}
