using AChen.Backend.Api.Data;
using AChen.Backend.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.Gacha;

public sealed class GachaService(
    AppDbContext db,
    GachaCsvSerializer csvSerializer,
    AllCardCsvSerializer allCardCsvSerializer,
    IGachaRandom random,
    ILogger<GachaService> logger)
{
    public const int MaxCsvBytes = 5 * 1024 * 1024;

    public async Task<GachaConfigResponse> GetAsync(CancellationToken cancellationToken)
    {
        var data = await LoadAsync(cancellationToken);
        return ToResponse(data);
    }

    public async Task<GachaConfigResponse> ImportAsync(ReadOnlyMemory<byte> content, CancellationToken cancellationToken)
    {
        if (content.Length is <= 0 or > MaxCsvBytes)
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "VALIDATION_ERROR",
                "请上传不超过 5 MiB 的有效 CSV 文件");
        }

        GachaConfigData imported;
        try
        {
            imported = csvSerializer.Deserialize(content);
        }
        catch (GachaCsvException exception)
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "VALIDATION_ERROR",
                exception.Message);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.GachaPoolEntries.ExecuteDeleteAsync(cancellationToken);
        await db.GachaRarityWeights.ExecuteDeleteAsync(cancellationToken);
        db.GachaPoolEntries.AddRange(imported.PoolEntries.Select(value => new GachaPoolEntry
        {
            PoolKey = value.PoolKey,
            CardId = value.CardId,
            Weight = value.Weight
        }));
        db.GachaRarityWeights.AddRange(imported.RarityWeights.Select(value => new GachaRarityWeight
        {
            Rarity = value.Rarity,
            Weight = value.Weight
        }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation(
            "Imported gacha config with {PoolEntryCount} pool entries and {RarityWeightCount} rarity weights.",
            imported.PoolEntries.Count,
            imported.RarityWeights.Count);
        return ToResponse(imported);
    }

    public async Task<AllCardsConfigResponse> GetAllCardsAsync(CancellationToken cancellationToken)
    {
        var cards = await LoadAllCardsAsync(cancellationToken);
        return ToAllCardsResponse(cards);
    }

    public async Task<AllCardsConfigResponse> ImportAllCardsAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken)
    {
        if (content.Length is <= 0 or > MaxCsvBytes)
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "VALIDATION_ERROR",
                "请上传不超过 5 MiB 的有效 CSV 文件");
        }

        AllCardsData imported;
        try
        {
            imported = allCardCsvSerializer.Deserialize(content);
        }
        catch (AllCardCsvException exception)
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "VALIDATION_ERROR",
                exception.Message);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.AllCards.ExecuteDeleteAsync(cancellationToken);
        db.AllCards.AddRange(imported.Cards.Select(value => new AllCard
        {
            CardId = value.CardId,
            SourcePool = value.SourcePool
        }));
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        logger.LogInformation("Imported all-cards catalog with {CardCount} cards.", imported.Cards.Count);
        return ToAllCardsResponse(imported.Cards);
    }

    public async Task<IReadOnlyList<GachaDrawResult>> DrawAsync(
        string poolKey,
        int count,
        CancellationToken cancellationToken)
    {
        var data = await LoadAsync(cancellationToken);
        if (data.RarityWeights.Count == 0 ||
            (data.PoolEntries.Count == 0 && !IsAllCardsPool(poolKey)))
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "GACHA_CONFIG_EMPTY",
                "尚未导入抽卡配置");
        }

        IReadOnlyList<WeightedCard> pool;
        if (IsAllCardsPool(poolKey))
        {
            var catalog = await LoadAllCardsAsync(cancellationToken);
            if (catalog.Count == 0)
            {
                throw new ApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "ALL_CARDS_EMPTY",
                    "尚未导入全部卡牌");
            }

            pool = catalog
                .Select(value => new WeightedCard(value.CardId, 100, value.SourcePool))
                .ToArray();
        }
        else
        {
            pool = data.PoolEntries
                .Where(value => string.Equals(value.PoolKey, poolKey, StringComparison.Ordinal))
                .Select(value => new WeightedCard(value.CardId, value.Weight, poolKey))
                .ToArray();
            if (pool.Count == 0)
            {
                throw new ApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "GACHA_POOL_NOT_FOUND",
                    "卡池不存在");
            }
        }

        var results = new List<GachaDrawResult>(count);
        for (var i = 0; i < count; i++)
        {
            var card = Pick(pool, value => value.Weight);
            var rarity = Pick(data.RarityWeights, value => value.Weight);
            results.Add(new GachaDrawResult(card.CardId, rarity.Rarity, card.SourcePool));
        }

        return results;
    }

    public async Task<GachaPoolResponse> GetPoolAsync(string poolKey, CancellationToken cancellationToken)
    {
        var key = poolKey?.Trim() ?? "";
        if (key.Length is < 1 or > 32)
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "GACHA_POOL_NOT_FOUND",
                "卡池不存在");
        }

        if (IsAllCardsPool(key))
        {
            var catalog = await LoadAllCardsAsync(cancellationToken);
            if (catalog.Count == 0)
            {
                throw new ApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "ALL_CARDS_EMPTY",
                    "尚未导入全部卡牌");
            }

            var allCards = catalog
                .GroupBy(value => value.CardId, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(value => value.CardId, StringComparer.Ordinal)
                .Select(value => new GachaPoolCardResponse(value.CardId, value.SourcePool))
                .ToArray();
            return new GachaPoolResponse(key, allCards);
        }

        var data = await LoadAsync(cancellationToken);
        if (data.PoolEntries.Count == 0)
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "GACHA_CONFIG_EMPTY",
                "尚未导入抽卡配置");
        }

        var cards = data.PoolEntries
            .Where(value => string.Equals(value.PoolKey, key, StringComparison.Ordinal))
            .GroupBy(value => value.CardId, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(value => value.CardId, StringComparer.Ordinal)
            .Select(value => new GachaPoolCardResponse(value.CardId, key))
            .ToArray();
        if (cards.Length == 0)
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "GACHA_POOL_NOT_FOUND",
                "卡池不存在");
        }

        return new GachaPoolResponse(key, cards);
    }

    private static bool IsAllCardsPool(string poolKey) =>
        string.Equals(poolKey, GachaPoolKeys.AllCards, StringComparison.Ordinal);

    private async Task<IReadOnlyList<AllCardResponse>> LoadAllCardsAsync(CancellationToken cancellationToken) =>
        await db.AllCards
            .AsNoTracking()
            .OrderBy(value => value.CardId)
            .Select(value => new AllCardResponse(value.CardId, value.SourcePool))
            .ToListAsync(cancellationToken);

    private async Task<GachaConfigData> LoadAsync(CancellationToken cancellationToken)
    {
        var poolEntries = await db.GachaPoolEntries
            .AsNoTracking()
            .OrderBy(value => value.PoolKey)
            .ThenBy(value => value.CardId)
            .Select(value => new GachaPoolEntryResponse(value.PoolKey, value.CardId, value.Weight))
            .ToListAsync(cancellationToken);
        var rarityWeights = await db.GachaRarityWeights
            .AsNoTracking()
            .OrderBy(value => value.Rarity)
            .Select(value => new GachaRarityWeightResponse(value.Rarity, value.Weight))
            .ToListAsync(cancellationToken);
        return new GachaConfigData(poolEntries, rarityWeights);
    }

    private T Pick<T>(IReadOnlyList<T> items, Func<T, int> weightSelector)
    {
        var total = 0;
        for (var i = 0; i < items.Count; i++)
        {
            total += weightSelector(items[i]);
        }

        var roll = random.Next(total);
        var cursor = 0;
        for (var i = 0; i < items.Count; i++)
        {
            cursor += weightSelector(items[i]);
            if (roll < cursor)
            {
                return items[i];
            }
        }

        return items[^1];
    }

    private static GachaConfigResponse ToResponse(GachaConfigData data) => new(
        data.PoolEntries.Count,
        data.RarityWeights.Count,
        data.PoolEntries,
        data.RarityWeights);

    private static AllCardsConfigResponse ToAllCardsResponse(IReadOnlyList<AllCardResponse> cards) =>
        new(cards.Count, cards);

    private readonly record struct WeightedCard(string CardId, int Weight, string SourcePool);
}
