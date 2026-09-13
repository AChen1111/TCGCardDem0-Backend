using AChen.Backend.Api.Features.Gacha;

namespace AChen.Backend.Api.Tests;

public sealed class AllCardCsvTests
{
    [Fact]
    public void Round_trip_keeps_unique_cards()
    {
        var serializer = new AllCardCsvSerializer();
        var source = new AllCardsData(
        [
            new AllCardResponse("26077389", "Card03"),
            new AllCardResponse("14558127", "Card01")
        ]);

        var restored = serializer.Deserialize(serializer.Serialize(source));

        Assert.Equal(2, restored.Cards.Count);
        Assert.Equal("14558127", restored.Cards[0].CardId);
        Assert.Equal("Card01", restored.Cards[0].SourcePool);
        Assert.Equal("26077389", restored.Cards[1].CardId);
        Assert.Equal("Card03", restored.Cards[1].SourcePool);
    }

    [Fact]
    public void Rejects_duplicate_card_id()
    {
        var serializer = new AllCardCsvSerializer();
        var csv = """
            CardId,SourcePool
            26077389,Card03
            26077389,Card01
            """;

        var exception = Assert.Throws<AllCardCsvException>(() => serializer.Deserialize(System.Text.Encoding.UTF8.GetBytes(csv)));
        Assert.Contains("不能重复", exception.Message);
    }
}
