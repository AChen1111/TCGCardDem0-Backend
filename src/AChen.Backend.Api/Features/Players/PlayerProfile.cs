using AChen.Backend.Api.Features.Auth;

namespace AChen.Backend.Api.Features.Players;

public sealed class PlayerProfile
{
    public Guid UserId { get; init; }
    public required string Nickname { get; set; }
    public int? AvatarId { get; set; }
    public long Gold { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public User User { get; init; } = null!;
}
