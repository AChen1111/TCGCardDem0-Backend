namespace AChen.Backend.Api.Features.AccountManagement;

public sealed record ManagedAccountSummary(
    Guid Id,
    string Username,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? Nickname,
    long? Gold,
    int RefreshSessionCount);

public sealed record ManagedAccountPage(
    IReadOnlyList<ManagedAccountSummary> Items,
    int Page,
    int PageSize,
    int TotalItems,
    int TotalPages);

public sealed record ManagedAccountDetails(
    Guid Id,
    string Username,
    string Nickname,
    int? AvatarId,
    IReadOnlyList<int> OwnedAvatarIds,
    int? BackgroundId,
    long Gold,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record UpdateManagedPlayerData(
    string Nickname,
    int? AvatarId,
    IReadOnlyList<int> OwnedAvatarIds,
    int? BackgroundId,
    long Gold);

public sealed record ManagedAccountUpdateResult(
    bool Found,
    IReadOnlyDictionary<string, string[]> Errors)
{
    public bool Succeeded => Found && Errors.Count == 0;
}
