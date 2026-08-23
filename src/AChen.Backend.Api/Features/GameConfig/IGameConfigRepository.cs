namespace AChen.Backend.Api.Features.GameConfig;

public interface IGameConfigRepository
{
    Task<GameConfigVersion?> GetDraftAsync(bool includeDefinitions, CancellationToken cancellationToken);
    Task<GameConfigVersion?> GetLatestPublishedAsync(bool includeDefinitions, CancellationToken cancellationToken);
    Task<bool> WasAvatarPublishedAsync(int id, CancellationToken cancellationToken);
    Task<bool> WasCardPackPublishedAsync(int id, CancellationToken cancellationToken);
    Task<bool> IsLatestPublishedAvatarEnabledAsync(int id, CancellationToken cancellationToken);
    void AddVersion(GameConfigVersion version);
    void RemoveAvatar(AvatarDefinition avatar);
    void RemoveCardPack(CardPackDefinition cardPack);
    Task SaveChangesAsync(CancellationToken cancellationToken);
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken);
}
