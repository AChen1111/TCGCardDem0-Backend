namespace AChen.Backend.Api.Features.Players;

public sealed record UpdatePlayerProfileRequest(
    string Nickname,
    int? AvatarId,
    int? BackgroundId,
    long ExpectedRevision);

public static class ShopCatalogTypes
{
    public const string Avatar = "avatar";
    public const string Wallpaper = "wallpaper";
}

public sealed record PurchaseShopItemRequest(
    string CatalogType,
    int ItemId,
    long ExpectedRevision);

public sealed record DrawCardsRequest(
    int PackId,
    string PoolKey,
    int Count,
    long ExpectedRevision);

public sealed record CardDrawResultResponse(string CardId, int Rarity, string SourcePool);

public sealed record DrawCardsResponse(
    IReadOnlyList<CardDrawResultResponse> Results,
    PlayerResponse Player);

public sealed record PlayerResponse(
    Guid Id,
    string Nickname,
    int? AvatarId,
    IReadOnlyList<int> OwnedAvatarIds,
    int? BackgroundId,
    IReadOnlyList<int> OwnedBackgroundIds,
    IReadOnlyList<OwnedCard> OwnedCards,
    long Gold,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
