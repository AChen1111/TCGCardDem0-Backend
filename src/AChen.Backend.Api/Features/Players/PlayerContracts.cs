namespace AChen.Backend.Api.Features.Players;

public sealed record UpdatePlayerProfileRequest(
    string Nickname,
    int? AvatarId,
    int? BackgroundId,
    long ExpectedRevision,
    int? AvatarFrameId = null);

public static class ShopCatalogTypes
{
    public const string Avatar = "avatar";
    public const string AvatarFrame = "avatar-frame";
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

public sealed record CardDrawResultResponse(string CardId, int Rarity, string SourcePool, bool IsOverflow, long UrGained);

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
    DateTimeOffset UpdatedAt,
    int AvatarFrameId,
    IReadOnlyList<int> OwnedAvatarFrameIds,
    long Ur);

public sealed record CraftCardRequest(string CardId, long ExpectedRevision, long ExpectedUrAmount);
public sealed record DismantleCardRequest(string CardId, int Rarity, int Count, long ExpectedRevision, long ExpectedUrAmount);
public sealed record CardWorkshopResponse(PlayerResponse Player, long UrAmount);
