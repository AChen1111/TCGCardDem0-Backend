using AChen.Backend.Api.Features.Players;

namespace AChen.Backend.Api.Features.Auth;

public sealed record RegisterRequest(string Username, string Password);

public sealed record LoginRequest(string Username, string Password);

public sealed record RefreshRequest(string RefreshToken);

public sealed record LogoutRequest(string RefreshToken);

public sealed record UserResponse(
    Guid Id,
    string Username,
    DateTimeOffset CreatedAt);

public sealed record AuthResponse(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    UserResponse User,
    PlayerResponse Player);
