namespace AChen.Backend.Api.Features.Auth;

public sealed record RegisterRequest(string Username, string Email, string Password);

public sealed record LoginRequest(string Identifier, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record UserResponse(
    Guid Id,
    string Username,
    string Email,
    DateTimeOffset CreatedAt);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    UserResponse User);
