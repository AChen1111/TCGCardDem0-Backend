using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Infrastructure;
using AChen.Configuration;
namespace AChen.Backend.Api.Features.Gacha;
public sealed class GachaService(PublishedConfigReader configReader, IGachaRandom random)
{
    public async Task<IReadOnlyList<GachaDrawResult>> DrawAsync(
        string poolKey,
        int count,
        CancellationToken cancellationToken)
    {
        var data = await LoadAsync(cancellationToken);
        if (data.RarityWeights.Length == 0 ||
            (data.PoolEntries.Length == 0 && !IsAllCardsPool(poolKey)))
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
            if (catalog.Length == 0)
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
                .Select(value => new WeightedCard(value.CardId, value.Weight, data.SourcePoolForArt(value.CardId)))
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
            bool special = data.SpecialMaterials.Any(value => value.CardId == card.CardId);
            var eligible = special
                ? data.RarityWeights.Where(value => value.Rarity == 0 || value.Rarity == 1 && data.SpecialMaterials.Any(x => x.CardId == card.CardId && x.AllowColorful)
                    || value.Rarity == 3 && data.SpecialMaterials.Any(x => x.CardId == card.CardId && x.AllowGoldOutline)).ToArray()
                : data.RarityWeights.Where(value => value.Rarity == 0).ToArray();
            var rarity = Pick(eligible, value => value.Weight);
            var arts = data.ArtVariants.Where(value => value.CardId == card.CardId).ToArray();
            int artIndex = random.Next(arts.Length + 1);
            string artId = artIndex == 0 ? card.CardId : arts[artIndex - 1].ArtId;
            results.Add(new GachaDrawResult(card.CardId, rarity.Rarity, data.SourcePoolForArt(artId), artId));
        }

        return results;
    }

    private static bool IsAllCardsPool(string key) => key == GachaPoolKeys.AllCards;
    private Task<PublishedGameConfig> LoadAsync(CancellationToken token) => configReader.GetAsync(token);
    private async Task<AllCardEntry[]> LoadAllCardsAsync(CancellationToken token) => (await configReader.GetAsync(token)).AllCards;
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

    private readonly record struct WeightedCard(string CardId, int Weight, string SourcePool);
}
