using System.Data;
using AChen.Backend.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.ContentDelivery;

public sealed class ContentReleaseRepository(AppDbContext db) : IContentReleaseRepository
{
    public async Task AddAsync(ContentRelease release, CancellationToken cancellationToken) =>
        await db.ContentReleases.AddAsync(release, cancellationToken);

    public Task<ContentRelease?> GetAsync(Guid id, bool includeFiles, CancellationToken cancellationToken)
    {
        IQueryable<ContentRelease> query = db.ContentReleases;
        if (includeFiles)
        {
            query = query.Include(value => value.Files);
        }

        return query.SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
    }

    public Task<ContentRelease?> FindAsync(
        string platform,
        string appVersion,
        string contentVersion,
        CancellationToken cancellationToken) =>
        db.ContentReleases.SingleOrDefaultAsync(value =>
                value.Platform == platform &&
                value.AppVersion == appVersion &&
                value.ContentVersion == contentVersion,
            cancellationToken);

    public async Task<(IReadOnlyList<ContentRelease> Items, int Total)> ListAsync(
        int page,
        int pageSize,
        string? platform,
        string? appVersion,
        ContentReleaseState? state,
        CancellationToken cancellationToken)
    {
        var query = db.ContentReleases.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(platform))
        {
            query = query.Where(value => value.Platform == platform);
        }

        if (!string.IsNullOrWhiteSpace(appVersion))
        {
            query = query.Where(value => value.AppVersion == appVersion);
        }

        if (state is not null)
        {
            query = query.Where(value => value.State == state);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(value => value.CreatedAt)
            .ThenByDescending(value => value.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<ActiveContentRelease?> GetActiveAsync(
        string channel,
        string platform,
        string appVersion,
        bool includeRelease,
        CancellationToken cancellationToken)
    {
        IQueryable<ActiveContentRelease> query = db.ActiveContentReleases;
        if (includeRelease)
        {
            query = query.Include(value => value.Release).ThenInclude(value => value.Files);
        }

        return query.SingleOrDefaultAsync(value =>
                value.Channel == channel &&
                value.Platform == platform &&
                value.AppVersion == appVersion,
            cancellationToken);
    }

    public async Task<ActiveContentRelease> SetActiveAsync(
        string channel,
        string platform,
        string appVersion,
        ContentRelease release,
        Guid? expectedCurrentReleaseId,
        string source,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        var active = await db.ActiveContentReleases.SingleOrDefaultAsync(value =>
                value.Channel == channel &&
                value.Platform == platform &&
                value.AppVersion == appVersion,
            cancellationToken);
        var actualCurrentReleaseId = active?.ReleaseId;
        if (actualCurrentReleaseId != expectedCurrentReleaseId)
        {
            throw new ContentDeliveryException(
                StatusCodes.Status409Conflict,
                "ACTIVE_RELEASE_CHANGED",
                "活动内容版本已发生变化，请刷新后重试");
        }

        if (active?.ReleaseId == release.Id)
        {
            await transaction.CommitAsync(cancellationToken);
            return active;
        }

        if (active is null)
        {
            active = new ActiveContentRelease
            {
                Channel = channel,
                Platform = platform,
                AppVersion = appVersion,
                ReleaseId = release.Id,
                Release = release,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.ActiveContentReleases.Add(active);
        }
        else
        {
            active.ReleaseId = release.Id;
            active.Release = release;
            active.UpdatedAt = now;
        }

        db.ContentPublications.Add(new ContentPublication
        {
            Channel = channel,
            Platform = platform,
            AppVersion = appVersion,
            PreviousReleaseId = actualCurrentReleaseId,
            ReleaseId = release.Id,
            Release = release,
            Source = source,
            CreatedAt = now,
            UpdatedAt = now
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return active;
    }

    public async Task<(IReadOnlyList<ContentPublication> Items, int Total)> ListPublicationsAsync(
        int page,
        int pageSize,
        string? channel,
        string? platform,
        string? appVersion,
        CancellationToken cancellationToken)
    {
        var query = db.ContentPublications.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(channel))
        {
            query = query.Where(value => value.Channel == channel);
        }

        if (!string.IsNullOrWhiteSpace(platform))
        {
            query = query.Where(value => value.Platform == platform);
        }

        if (!string.IsNullOrWhiteSpace(appVersion))
        {
            query = query.Where(value => value.AppVersion == appVersion);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Include(value => value.Release)
            .OrderByDescending(value => value.CreatedAt)
            .ThenByDescending(value => value.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return (items, total);
    }

    public Task<ContentReleaseFile?> GetFileAsync(
        Guid releaseId,
        string relativePath,
        CancellationToken cancellationToken) =>
        db.ContentReleaseFiles
            .AsNoTracking()
            .Include(value => value.Release)
            .SingleOrDefaultAsync(value =>
                    value.ReleaseId == releaseId &&
                    value.RelativePath == relativePath,
                cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);

    public void Delete(ContentRelease release) => db.ContentReleases.Remove(release);
}
