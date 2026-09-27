using AChen.Backend.Api.Features.Players;

namespace AChen.Backend.Api.Features.Social;

public sealed record FriendSummary(Guid Id, string Nickname, int? AvatarId, int AvatarFrameId);

public sealed record FriendSearchHit(Guid Id, string Nickname, int? AvatarId, bool IsFriend, bool IsPending, int AvatarFrameId);

public sealed record CreateFriendRequestBody(Guid TargetPlayerId);

public sealed record FriendRequestCreated(Guid Id, Guid TargetPlayerId);

public sealed record InboxItem(
    string Kind,
    Guid Id,
    DateTimeOffset CreatedAt,
    Guid? PlayerId,
    string? Nickname,
    int? AvatarId,
    long Gold,
    IReadOnlyList<OwnedCard> Cards,
    string? TitleKey = null, int? AvatarFrameId = null);

public sealed record ClaimGiftRequest(long ExpectedRevision);

public sealed record AdminGiftCard(string CardId, int Count, int Rarity = 0);

public sealed record AdminGrantGiftRequest(
    string Username,
    long Gold,
    IReadOnlyList<AdminGiftCard>? Cards,
    string? TitleKey = null);

public sealed record AdminGrantGiftResponse(Guid GiftId, Guid TargetPlayerId);
