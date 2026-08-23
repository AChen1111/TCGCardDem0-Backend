using AChen.Backend.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.GameConfig;

public sealed class GameConfigRepository(AppDbContext db) : IGameConfigRepository
{
    public Task<GameConfigVersion?> GetDraftAsync(
        bool includeDefinitions,
        CancellationToken cancellationToken) =>
        BuildVersionQuery(includeDefinitions)
            .SingleOrDefaultAsync(value => value.State == GameConfigVersionState.Draft, cancellationToken);

    public Task<GameConfigVersion?> GetLatestPublishedAsync(
        bool includeDefinitions,
        CancellationToken cancellationToken) =>
        BuildVersionQuery(includeDefinitions)
            .Where(value => value.State == GameConfigVersionState.Published)
            .OrderByDescending(value => value.Revision)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<bool> WasAvatarPublishedAsync(int id, CancellationToken cancellationToken) =>
        db.AvatarDefinitions.AnyAsync(
            avatar => avatar.Id == id && avatar.Version.State == GameConfigVersionState.Published,
            cancellationToken);

    public Task<bool> WasCardPackPublishedAsync(int id, CancellationToken cancellationToken) =>
        db.CardPackDefinitions.AnyAsync(
            cardPack => cardPack.Id == id && cardPack.Version.State == GameConfigVersionState.Published,
            cancellationToken);

    public async Task<bool> IsLatestPublishedAvatarEnabledAsync(
        int id,
        CancellationToken cancellationToken)
    {
        var revision = await db.GameConfigVersions
            .Where(value => value.State == GameConfigVersionState.Published)
            .MaxAsync(value => (long?)value.Revision, cancellationToken);
        return revision is not null && await db.AvatarDefinitions.AnyAsync(
            avatar => avatar.Revision == revision && avatar.Id == id && avatar.IsEnabled,
            cancellationToken);
    }

    public void AddVersion(GameConfigVersion version) => db.GameConfigVersions.Add(version);

    public void RemoveAvatar(AvatarDefinition avatar) => db.AvatarDefinitions.Remove(avatar);

    public void RemoveCardPack(CardPackDefinition cardPack) => db.CardPackDefinitions.Remove(cardPack);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<Task<T>> action,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var result = await action();
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private IQueryable<GameConfigVersion> BuildVersionQuery(bool includeDefinitions)
    {
        IQueryable<GameConfigVersion> query = db.GameConfigVersions;
        return includeDefinitions
            ? query.Include(value => value.Avatars).Include(value => value.CardPacks).AsSplitQuery()
            : query;
    }
}
