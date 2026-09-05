namespace AChen.Backend.Api.Features.Players;

public sealed record UpdatePlayerProfileRequest(
    string Nickname,
    int? AvatarId,
    int? BackgroundId,
    long ExpectedRevision);

public sealed record PlayerResponse(
    Guid Id,
    string Nickname,
    int? AvatarId,
    IReadOnlyList<int> OwnedAvatarIds,
    int? BackgroundId,
    long Gold,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
