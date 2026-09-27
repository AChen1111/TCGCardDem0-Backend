using AChen.Configuration;
using AChen.Backend.Api.Infrastructure;

namespace AChen.Backend.Api.Features.Players;

public sealed record CardGrantResult(string CardId, int Rarity, int Kept, int Overflow, long Ur);
public sealed record InventorySettlement(List<OwnedCard> Cards, IReadOnlyList<CardGrantResult> Results, long UrGained);

public static class CardInventorySettlement
{
    public static InventorySettlement Grant(IEnumerable<OwnedCard> existing, IEnumerable<OwnedCard> grants, CardEconomyConfiguration rules)
    {
        var cards = existing.ToDictionary(x => (x.CardId, x.Rarity));
        var results = new List<CardGrantResult>();
        long ur = 0;
        foreach (var grant in grants)
        {
            if (grant.Rarity < 0 || grant.Rarity > 4 || grant.Count <= 0)
                throw new ApiException(422, "INVALID_CARD_GRANT", "发卡版本或数量无效");
            var key = (grant.CardId, grant.Rarity);
            int owned = cards.TryGetValue(key, out var card) ? card.Count : 0;
            CardGrantAmounts result;
            try { result = rules.Grant(owned, grant.Count, grant.Rarity); }
            catch (OverflowException) { throw new ApiException(422, "UR_OVERFLOW", "UR 数量超出上限"); }
            if (result.Kept > 0) cards[key] = new OwnedCard(grant.CardId, grant.Rarity, owned + result.Kept);
            ur = AddUr(ur, result.Ur);
            results.Add(new CardGrantResult(grant.CardId, grant.Rarity, result.Kept, result.Overflow, result.Ur));
        }
        return new InventorySettlement(cards.Values.OrderBy(x => x.CardId, StringComparer.Ordinal).ThenBy(x => x.Rarity).ToList(), results, ur);
    }

    public static long AddUr(long current, long amount)
    {
        if (amount < 0 || current > long.MaxValue - amount) throw new ApiException(422, "UR_OVERFLOW", "UR 数量超出上限");
        return current + amount;
    }
}
