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

public sealed record PlayerResponse(
    Guid Id,
    string Nickname,
    int? AvatarId,
    IReadOnlyList<int> OwnedAvatarIds,
    int? BackgroundId,
    IReadOnlyList<int> OwnedBackgroundIds,
    long Gold,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
