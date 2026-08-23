using AChen.Backend.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.Players;

public sealed class PlayerRepository(AppDbContext db) : IPlayerRepository
{
    public async Task<PlayerProfile?> GetOrCreateAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var existing = await db.PlayerProfiles.SingleOrDefaultAsync(
            value => value.UserId == userId,
            cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var username = await db.Users
            .Where(value => value.Id == userId)
            .Select(value => value.Username)
            .SingleOrDefaultAsync(cancellationToken);
        if (username is null)
        {
            return null;
        }

        var profile = new PlayerProfile
        {
            UserId = userId,
            Nickname = username,
            Gold = 0,
            Revision = 0,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.PlayerProfiles.Add(profile);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return profile;
        }
        catch (DbUpdateException exception) when (
            exception.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            db.Entry(profile).State = EntityState.Detached;
            return await db.PlayerProfiles.SingleOrDefaultAsync(
                value => value.UserId == userId,
                cancellationToken);
        }
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);
}
