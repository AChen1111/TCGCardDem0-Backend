namespace AChen.Backend.Api.Features.ContentDelivery;

public interface IContentReleaseRepository
{
    Task AddAsync(ContentRelease release, CancellationToken cancellationToken);
    Task<ContentRelease?> GetAsync(Guid id, bool includeFiles, CancellationToken cancellationToken);
    Task<ContentRelease?> FindAsync(
        string platform,
        string appVersion,
        string contentVersion,
        CancellationToken cancellationToken);
    Task<(IReadOnlyList<ContentRelease> Items, int Total)> ListAsync(
        int page,
        int pageSize,
        string? platform,
        string? appVersion,
        ContentReleaseState? state,
        CancellationToken cancellationToken);
    Task<ActiveContentRelease?> GetActiveAsync(
        string channel,
        string platform,
        string appVersion,
        bool includeRelease,
        CancellationToken cancellationToken);
    Task<ActiveContentRelease> SetActiveAsync(
        string channel,
        string platform,
        string appVersion,
        ContentRelease release,
        Guid? expectedCurrentReleaseId,
        string source,
        DateTimeOffset now,
        CancellationToken cancellationToken);
    Task<(IReadOnlyList<ContentPublication> Items, int Total)> ListPublicationsAsync(
        int page,
        int pageSize,
        string? channel,
        string? platform,
        string? appVersion,
        CancellationToken cancellationToken);
    Task<ContentReleaseFile?> GetFileAsync(
        Guid releaseId,
        string relativePath,
        CancellationToken cancellationToken);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    void Delete(ContentRelease release);
}
