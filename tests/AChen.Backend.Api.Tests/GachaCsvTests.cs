using AChen.Backend.Api.Features.Gacha;

namespace AChen.Backend.Api.Tests;

public sealed class GachaCsvTests
{
    [Fact]
    public void Round_trip_keeps_pool_and_rarity_rows()
    {
        var serializer = new GachaCsvSerializer();
        var source = new GachaConfigData(
            [new GachaPoolEntryResponse("Card01", "26077389", 100)],
            [new GachaRarityWeightResponse(0, 90), new GachaRarityWeightResponse(1, 10)]);

        var restored = serializer.Deserialize(serializer.Serialize(source));

        Assert.Equal("Card01", Assert.Single(restored.PoolEntries).PoolKey);
        Assert.Equal("26077389", restored.PoolEntries[0].CardId);
        Assert.Equal(2, restored.RarityWeights.Count);
        Assert.Equal(90, restored.RarityWeights.Single(value => value.Rarity == 0).Weight);
    }

    [Fact]
    public void Rejects_duplicate_card_in_same_pool()
    {
        var serializer = new GachaCsvSerializer();
        var csv = """
            Table,PoolKey,CardId,Weight,Rarity
            Card,Card01,26077389,100,
            Card,Card01,26077389,50,
            Rarity,,,90,0
            """;

        var exception = Assert.Throws<GachaCsvException>(() => serializer.Deserialize(System.Text.Encoding.UTF8.GetBytes(csv)));
        Assert.Contains("不能重复", exception.Message);
    }

    [Fact]
    public void Rejects_card_all_in_weighted_pool_table()
    {
        var serializer = new GachaCsvSerializer();
        var csv = """
            Table,PoolKey,CardId,Weight,Rarity
            Card,CardAll,26077389,100,
            Rarity,,,90,0
            """;

        var exception = Assert.Throws<GachaCsvException>(() => serializer.Deserialize(System.Text.Encoding.UTF8.GetBytes(csv)));
        Assert.Contains("全部卡牌表", exception.Message);
    }
}
