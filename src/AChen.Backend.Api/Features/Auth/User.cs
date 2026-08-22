namespace AChen.Backend.Api.Features.Auth;

public sealed class User
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string Username { get; init; }
    public required string NormalizedUsername { get; init; }
    public required string Email { get; init; }
    public required string NormalizedEmail { get; init; }
    public required string PasswordHash { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<RefreshSession> RefreshSessions { get; init; } = [];
}
