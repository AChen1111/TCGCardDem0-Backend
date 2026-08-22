using System.IdentityModel.Tokens.Jwt;

namespace AChen.Backend.Api.Features.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/auth")
            .RequireRateLimiting("auth")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });

        group.MapPost("/register", RegisterAsync);
        group.MapPost("/login", LoginAsync);
        group.MapPost("/refresh", RefreshAsync);
        group.MapPost("/logout", LogoutAsync);
        group.MapGet("/me", GetCurrentUserAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> RegisterAsync(
        RegisterRequest request,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = AuthValidation.Validate(request);
        if (errors.Count > 0)
        {
            return ValidationProblem(errors);
        }

        var response = await authService.RegisterAsync(request, cancellationToken);
        return Results.Created("/api/auth/me", response);
    }

    private static async Task<IResult> LoginAsync(
        LoginRequest request,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = AuthValidation.Validate(request);
        if (errors.Count > 0)
        {
            return ValidationProblem(errors);
        }

        return Results.Ok(await authService.LoginAsync(request, cancellationToken));
    }

    private static async Task<IResult> RefreshAsync(
        RefreshRequest request,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = AuthValidation.ValidateRefreshToken(request.RefreshToken);
        if (errors.Count > 0)
        {
            return ValidationProblem(errors);
        }

        return Results.Ok(await authService.RefreshAsync(request.RefreshToken, cancellationToken));
    }

    private static async Task<IResult> LogoutAsync(
        LogoutRequest request,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var errors = AuthValidation.ValidateRefreshToken(request.RefreshToken);
        if (errors.Count > 0)
        {
            return ValidationProblem(errors);
        }

        await authService.LogoutAsync(request.RefreshToken, cancellationToken);
        return Results.NoContent();
    }

    private static async Task<IResult> GetCurrentUserAsync(
        HttpContext context,
        AuthService authService,
        CancellationToken cancellationToken)
    {
        var subject = context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value;
        if (!Guid.TryParse(subject, out var userId))
        {
            return Results.Unauthorized();
        }

        return Results.Ok(await authService.GetUserAsync(userId, cancellationToken));
    }

    private static IResult ValidationProblem(Dictionary<string, string[]> errors) =>
        Results.ValidationProblem(
            errors,
            statusCode: StatusCodes.Status422UnprocessableEntity,
            title: "Request validation failed.",
            extensions: new Dictionary<string, object?> { ["code"] = "VALIDATION_ERROR" });
}
