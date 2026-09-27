using System.Text.Json;
using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Infrastructure;
using AChen.Configuration;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.Players;

public sealed class CardWorkshopService(AppDbContext db, PublishedConfigReader configs, TimeProvider clock)
{
    public async Task<CardWorkshopResponse> CraftAsync(Guid userId, CraftCardRequest request, CancellationToken ct)
    {
        var config = await configs.GetAsync(ct);
        var rules = CardEconomyConfiguration.From(config);
        if (!config.AllCards.Any(x => x.CardId == request.CardId)) throw Error("CARD_NOT_FOUND", "卡牌不存在");
        RequireQuote(request.ExpectedUrAmount, rules.CraftCostUr);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var player = await PlayerAsync(userId, request.ExpectedRevision, ct);
        if (player.OwnedCards.Any(x => x.CardId == request.CardId && x.Rarity == 0 && x.Count > 0))
            throw Error("NORMAL_CARD_ALREADY_OWNED", "已拥有该卡普通版");
        if (player.Ur < rules.CraftCostUr) throw Error("INSUFFICIENT_UR", "UR 不足");
        player.OwnedCards = player.OwnedCards.Append(new OwnedCard(request.CardId, 0, 1)).OrderBy(x => x.CardId).ThenBy(x => x.Rarity).ToList();
        player.Ur -= rules.CraftCostUr;
        await SaveAsync(player, ct);
        await transaction.CommitAsync(ct);
        return new CardWorkshopResponse(PlayerService.ToResponse(player), rules.CraftCostUr);
    }

    public async Task<CardWorkshopResponse> DismantleAsync(Guid userId, DismantleCardRequest request, CancellationToken ct)
    {
        if (request.Rarity < 0 || request.Rarity > 4 || request.Count <= 0)
            throw Error("INVALID_DISMANTLE_COUNT", "分解版本或数量无效");
        var rules = CardEconomyConfiguration.From(await configs.GetAsync(ct));
        long reward;
        try { reward = checked(request.Count * rules.DismantleUr(request.Rarity)); }
        catch (OverflowException) { throw Error("UR_OVERFLOW", "UR 数量超出上限"); }
        RequireQuote(request.ExpectedUrAmount, reward);
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var player = await PlayerAsync(userId, request.ExpectedRevision, ct);
        var card = player.OwnedCards.SingleOrDefault(x => x.CardId == request.CardId && x.Rarity == request.Rarity);
        if (card is null || card.Count < request.Count) throw Error("CARD_NOT_OWNED", "持有数量不足");
        int remaining = card.Count - request.Count;
        var decks = await db.PlayerDecks.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
        var affected = decks.Where(deck =>
            JsonSerializer.Deserialize<Decks.DeckCardEntry[]>(deck.MainDeckJson)!
                .Concat(JsonSerializer.Deserialize<Decks.DeckCardEntry[]>(deck.ExtraDeckJson)!)
                .Where(x => x.CardId == request.CardId && x.Rarity == request.Rarity).Sum(x => (long)x.Count) > remaining)
            .Select(x => x.Name).ToArray();
        if (affected.Length > 0) throw new CardInDeckException(affected);
        long ur = CardInventorySettlement.AddUr(player.Ur, reward);
        player.OwnedCards = player.OwnedCards.Where(x => x != card).ToList();
        if (remaining > 0) player.OwnedCards.Add(card with { Count = remaining });
        player.OwnedCards = player.OwnedCards.OrderBy(x => x.CardId).ThenBy(x => x.Rarity).ToList();
        player.Ur = ur;
        await SaveAsync(player, ct);
        await transaction.CommitAsync(ct);
        return new CardWorkshopResponse(PlayerService.ToResponse(player), reward);
    }

    async Task<PlayerProfile> PlayerAsync(Guid id, long revision, CancellationToken ct)
    {
        var player = await db.PlayerProfiles.SingleAsync(x => x.UserId == id, ct);
        if (revision < 0 || player.Revision != revision) throw new ApiException(409, "PLAYER_DATA_CHANGED", "玩家数据已发生变化");
        return player;
    }

    async Task SaveAsync(PlayerProfile player, CancellationToken ct)
    {
        player.Revision++;
        player.UpdatedAt = clock.GetUtcNow();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new ApiException(409, "PLAYER_DATA_CHANGED", "玩家数据已发生变化"); }
    }

    static void RequireQuote(long expected, long actual)
    {
        if (expected != actual) throw new CardPriceChangedException(actual);
    }
    static ApiException Error(string code, string message) => new(422, code, message);
}

public sealed class CardInDeckException(string[] decks) : ApiException(422, "CARD_IN_DECK", "分解后将不满足已保存卡组"), IApiValidationException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = new Dictionary<string, string[]> { ["decks"] = decks };
}

public sealed class CardPriceChangedException(long amount) : ApiException(409, "CARD_PRICE_CHANGED", "卡牌报价已变化，请重新确认"), IApiValidationException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = new Dictionary<string, string[]> { ["urAmount"] = [amount.ToString(System.Globalization.CultureInfo.InvariantCulture)] };
}
