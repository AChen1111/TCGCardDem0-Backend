using AChen.Backend.Api.Features.Auth;

namespace AChen.Backend.Api.Features.Players;

public sealed class PlayerProfile
{
    public const int DefaultAvatarId = 1010001;
    public const int DefaultAvatarFrameId = 1030001;
    public const int DefaultBackgroundId = 1;

    public Guid UserId { get; init; }
    public required string Nickname { get; set; }
    public int? AvatarId { get; set; }
    public int AvatarFrameId { get; set; } = DefaultAvatarFrameId;
    public List<int> OwnedAvatarFrameIds { get; set; } = [DefaultAvatarFrameId];
    public List<int> OwnedAvatarIds { get; set; } = [];
    public int? BackgroundId { get; set; }
    public List<int> OwnedBackgroundIds { get; set; } = [];
    public List<OwnedCard> OwnedCards { get; set; } = [];
    public List<string> OwnedArtIds { get; set; } = [];
    public long Gold { get; set; }
    public long Ur { get; set; }
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
        OwnedBackgroundIds = [DefaultBackgroundId],
        OwnedCards = [],
        Gold = 0,
        Revision = 0,
        CreatedAt = now,
        UpdatedAt = now
    };
}
