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
