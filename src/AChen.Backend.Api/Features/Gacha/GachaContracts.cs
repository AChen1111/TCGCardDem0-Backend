namespace AChen.Backend.Api.Features.Gacha;

public static class GachaPoolKeys
{
    public const string AllCards = "CardAll";
}

public sealed record AllCardResponse(string CardId, string SourcePool);

public sealed record AllCardsData(IReadOnlyList<AllCardResponse> Cards);

public sealed record AllCardsConfigResponse(
    int CardCount,
    IReadOnlyList<AllCardResponse> Cards);

public sealed record GachaPoolEntryResponse(string PoolKey, string CardId, int Weight);

public sealed record GachaRarityWeightResponse(int Rarity, int Weight);

public sealed record GachaConfigResponse(
    int PoolEntryCount,
    int RarityWeightCount,
    IReadOnlyList<GachaPoolEntryResponse> PoolEntries,
    IReadOnlyList<GachaRarityWeightResponse> RarityWeights);

public sealed record GachaConfigData(
    IReadOnlyList<GachaPoolEntryResponse> PoolEntries,
    IReadOnlyList<GachaRarityWeightResponse> RarityWeights);

public sealed record GachaDrawResult(string CardId, int Rarity, string SourcePool);
