namespace AChen.Backend.Api.Features.Players;

public interface IPlayerRepository
{
    Task<PlayerProfile?> GetOrCreateAsync(
        Guid userId,
        DateTimeOffset now,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
