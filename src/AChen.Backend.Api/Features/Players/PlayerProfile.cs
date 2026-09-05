using AChen.Backend.Api.Features.Auth;

namespace AChen.Backend.Api.Features.Players;

public sealed class PlayerProfile
{
    public const int DefaultAvatarId = 0;
    public const int DefaultBackgroundId = 1;

    public Guid UserId { get; init; }
    public required string Nickname { get; set; }
    public int? AvatarId { get; set; }
    public List<int> OwnedAvatarIds { get; set; } = [];
    public int? BackgroundId { get; set; }
    public long Gold { get; set; }
    public long Revision { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public User User { get; init; } = null!;

    public static PlayerProfile ForNewAccount(Guid userId, string username, DateTimeOffset now) => new()
    {
        UserId = userId,
        Nickname = username,
        AvatarId = DefaultAvatarId,
        BackgroundId = DefaultBackgroundId,
        OwnedAvatarIds = [DefaultAvatarId],
        Gold = 0,
        Revision = 0,
        CreatedAt = now,
        UpdatedAt = now
    };
}
