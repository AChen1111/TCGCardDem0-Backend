namespace AChen.Backend.Api.Features.Players;

public sealed record UpdatePlayerProfileRequest(
    string Nickname,
    int? AvatarId,
    long ExpectedRevision);

public sealed record PlayerResponse(
    Guid Id,
    string Nickname,
    int? AvatarId,
    long Gold,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
