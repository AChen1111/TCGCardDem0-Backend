using AChen.Configuration;
using AChen.Backend.Api.Infrastructure;
using AChen.Backend.Api.Features.Gacha;
using AChen.Backend.Api.Features.ContentDelivery;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.Players;

public sealed class PlayerService(
    IPlayerRepository repository,
    PublishedConfigReader configReader,
    GachaService gachaService,
    TimeProvider timeProvider,
    ILogger<PlayerService> logger)
{
    public async Task<PlayerResponse> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var profile = await GetRequiredAsync(userId, cancellationToken);
        return ToResponse(profile);
    }

    public async Task<PlayerResponse> UpdateProfileAsync(
        Guid userId,
        UpdatePlayerProfileRequest request,
        CancellationToken cancellationToken)
    {
        var errors = PlayerValidation.Validate(request);
        if (errors.Count > 0)
        {
            throw new PlayerValidationException(errors);
        }

        var profile = await GetRequiredAsync(userId, cancellationToken);
        if (profile.Revision != request.ExpectedRevision)
        {
            throw Changed();
        }

        if (request.AvatarId != profile.AvatarId && request.AvatarId is int avatarId)
        {
            await EnsureAvatarAvailableAsync(avatarId, cancellationToken);
            if (!profile.OwnedAvatarIds.Contains(avatarId))
            {
                throw new ApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "AVATAR_NOT_OWNED",
                    "尚未拥有该头像");
            }
        }

        if (request.BackgroundId != profile.BackgroundId && request.BackgroundId is int backgroundId)
        {
            await EnsureWallpaperAvailableAsync(backgroundId, cancellationToken);
            if (!profile.OwnedBackgroundIds.Contains(backgroundId))
            {
                throw new ApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "WALLPAPER_NOT_OWNED",
                    "尚未拥有该壁纸");
            }
        }

        if (request.AvatarFrameId is int frameId && frameId != profile.AvatarFrameId)
        {
            var frame = (await configReader.GetAsync(cancellationToken)).Catalog.AvatarFrames.SingleOrDefault(x => x.Id == frameId && x.IsEnabled);
            if (frame is null) throw new ApiException(422, "AVATAR_FRAME_NOT_AVAILABLE", "该头像框不存在或尚未启用");
            if (!profile.OwnedAvatarFrameIds.Contains(frameId)) throw new ApiException(422, "AVATAR_FRAME_NOT_OWNED", "尚未拥有该头像框");
            profile.AvatarFrameId = frameId;
        }
        profile.Nickname = request.Nickname.Trim();
        profile.AvatarId = request.AvatarId;
        profile.BackgroundId = request.BackgroundId;
        profile.Revision++;
        profile.UpdatedAt = timeProvider.GetUtcNow();
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Changed();
        }

        logger.LogInformation(
            "Updated player profile {UserId} to revision {Revision}.",
            userId,
            profile.Revision);
        return ToResponse(profile);
    }

    public async Task<PlayerResponse> PurchaseShopItemAsync(
        Guid userId,
        PurchaseShopItemRequest request,
        CancellationToken cancellationToken)
    {
        var errors = PlayerValidation.Validate(request);
        if (errors.Count > 0)
        {
            throw new PlayerValidationException(errors);
        }

        var catalogType = request.CatalogType.Trim();
        var published = (await configReader.GetAsync(cancellationToken)).Catalog;
        var now = timeProvider.GetUtcNow();
        long priceGold;
        if (catalogType == ShopCatalogTypes.Avatar)
        {
            var avatar = published.Avatars.FirstOrDefault(value => value.Id == request.ItemId);
            if (avatar is null || !IsOnSale(avatar.IsEnabled, avatar.StartsAt, avatar.EndsAt, now))
            {
                throw new ApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "AVATAR_NOT_AVAILABLE",
                    "该头像不存在或尚未启用");
            }

            priceGold = avatar.PriceGold;
        }
        else if (catalogType == ShopCatalogTypes.AvatarFrame)
        {
            var frame = published.AvatarFrames.SingleOrDefault(x => x.Id == request.ItemId);
            if (frame is null || !IsOnSale(frame.IsEnabled, frame.StartsAt, frame.EndsAt, now))
                throw new ApiException(422, "AVATAR_FRAME_NOT_AVAILABLE", "该头像框不存在或尚未启用");
            priceGold = frame.PriceGold;
        }
        else
        {
            var wallpaper = published.Wallpapers.FirstOrDefault(value => value.Id == request.ItemId);
            if (wallpaper is null || !IsOnSale(wallpaper.IsEnabled, wallpaper.StartsAt, wallpaper.EndsAt, now))
            {
                throw new ApiException(
                    StatusCodes.Status422UnprocessableEntity,
                    "WALLPAPER_NOT_AVAILABLE",
                    "该壁纸不存在或尚未启用");
            }

            priceGold = wallpaper.PriceGold;
        }

        var profile = await GetRequiredAsync(userId, cancellationToken);
        if (profile.Revision != request.ExpectedRevision)
        {
            throw Changed();
        }

        var alreadyOwned = catalogType == ShopCatalogTypes.Avatar
            ? profile.OwnedAvatarIds.Contains(request.ItemId)
            : catalogType == ShopCatalogTypes.AvatarFrame ? profile.OwnedAvatarFrameIds.Contains(request.ItemId)
            : profile.OwnedBackgroundIds.Contains(request.ItemId);
        if (alreadyOwned)
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "ITEM_ALREADY_OWNED",
                "已拥有该商品");
        }

        if (profile.Gold < priceGold)
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "INSUFFICIENT_GOLD",
                "金币不足");
        }

        profile.Gold -= priceGold;
        if (catalogType == ShopCatalogTypes.Avatar)
        {
            profile.OwnedAvatarIds = profile.OwnedAvatarIds.Append(request.ItemId).OrderBy(value => value).ToList();
        }
        else if (catalogType == ShopCatalogTypes.AvatarFrame)
        {
            profile.OwnedAvatarFrameIds = profile.OwnedAvatarFrameIds.Append(request.ItemId).OrderBy(x => x).ToList();
        }
        else
        {
            profile.OwnedBackgroundIds = profile.OwnedBackgroundIds.Append(request.ItemId).OrderBy(value => value).ToList();
        }

        profile.Revision++;
        profile.UpdatedAt = now;
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Changed();
        }

        logger.LogInformation(
            "Player {UserId} purchased {CatalogType} {ItemId} for {PriceGold} gold at revision {Revision}; content {ContentRelease}.",
            userId,
            catalogType,
            request.ItemId,
            priceGold,
            profile.Revision,
            configReader.ReleaseId);
        return ToResponse(profile);
    }

    public async Task<DrawCardsResponse> DrawCardsAsync(
        Guid userId,
        DrawCardsRequest request,
        CancellationToken cancellationToken)
    {
        var errors = PlayerValidation.Validate(request);
        if (errors.Count > 0)
        {
            throw new PlayerValidationException(errors);
        }

        if (request.Count is < 1 or > 10)
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "INVALID_DRAW_COUNT",
                "一次抽取数量须为 1-10");
        }

        var configuration = await configReader.GetAsync(cancellationToken);
        var published = configuration.Catalog;
        var now = timeProvider.GetUtcNow();
        var pack = published.CardPacks.FirstOrDefault(value => value.Id == request.PackId);
        if (pack is null || !IsOnSale(pack.IsEnabled, pack.StartsAt, pack.EndsAt, now))
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "CARD_PACK_NOT_AVAILABLE",
                "该卡包不存在或尚未启用");
        }

        var poolKey = request.PoolKey.Trim();
        if (!string.Equals(pack.PoolKey, poolKey, StringComparison.Ordinal))
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "CARD_PACK_POOL_MISMATCH",
                "卡包与卡池不匹配");
        }

        var profile = await GetRequiredAsync(userId, cancellationToken);
        if (profile.Revision != request.ExpectedRevision)
        {
            throw Changed();
        }

        if (profile.Gold < pack.PriceGold)
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "INSUFFICIENT_GOLD",
                "金币不足");
        }

        var draws = await gachaService.DrawAsync(pack.PoolKey, request.Count, cancellationToken);
        var settlement = CardInventorySettlement.Grant(profile.OwnedCards, draws.Select(x => new OwnedCard(x.CardId, x.Rarity, 1)), CardEconomyConfiguration.From(configuration));
        long ur = CardInventorySettlement.AddUr(profile.Ur, settlement.UrGained);
        profile.Gold -= pack.PriceGold;
        profile.OwnedCards = settlement.Cards;
        profile.Ur = ur;
        profile.Revision++;
        profile.UpdatedAt = now;
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Changed();
        }

        logger.LogInformation(
            "Player {UserId} drew {Count} cards from pack {PackId} pool {PoolKey} for {PriceGold} gold at revision {Revision}; content {ContentRelease}.",
            userId,
            request.Count,
            pack.Id,
            pack.PoolKey,
            pack.PriceGold,
            profile.Revision,
            configReader.ReleaseId);
        return new DrawCardsResponse(
            draws.Select((value, i) => new CardDrawResultResponse(value.CardId, value.Rarity, value.SourcePool, settlement.Results[i].Overflow > 0, settlement.Results[i].Ur)).ToArray(),
            ToResponse(profile));
    }

    private async Task EnsureAvatarAvailableAsync(int avatarId, CancellationToken cancellationToken)
    {
        if (!(await configReader.GetAsync(cancellationToken)).Catalog.Avatars.Any(x => x.Id == avatarId && IsOnSale(x.IsEnabled, x.StartsAt, x.EndsAt, timeProvider.GetUtcNow())))
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "AVATAR_NOT_AVAILABLE",
                "该头像不存在或尚未启用");
        }
    }

    private async Task EnsureWallpaperAvailableAsync(int wallpaperId, CancellationToken cancellationToken)
    {
        if (!(await configReader.GetAsync(cancellationToken)).Catalog.Wallpapers.Any(x => x.Id == wallpaperId && IsOnSale(x.IsEnabled, x.StartsAt, x.EndsAt, timeProvider.GetUtcNow())))
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "WALLPAPER_NOT_AVAILABLE",
                "该壁纸不存在或尚未启用");
        }
    }

    private static bool IsOnSale(
        bool isEnabled,
        DateTimeOffset? startsAt,
        DateTimeOffset? endsAt,
        DateTimeOffset now) =>
        isEnabled &&
        (!startsAt.HasValue || startsAt <= now) &&
        (!endsAt.HasValue || endsAt > now);

    private async Task<PlayerProfile> GetRequiredAsync(Guid userId, CancellationToken cancellationToken) =>
        await repository.GetOrCreateAsync(userId, timeProvider.GetUtcNow(), cancellationToken) ??
        throw new ApiException(
            StatusCodes.Status401Unauthorized,
            "INVALID_ACCESS_TOKEN",
            "登录状态已失效，请重新登录");

    internal static PlayerResponse ToResponse(PlayerProfile profile) => new(
        profile.UserId,
        profile.Nickname,
        profile.AvatarId,
        profile.OwnedAvatarIds.ToArray(),
        profile.BackgroundId,
        profile.OwnedBackgroundIds.ToArray(),
        profile.OwnedCards.ToArray(),
        profile.Gold,
        profile.Revision,
        profile.CreatedAt,
        profile.UpdatedAt,
        profile.AvatarFrameId,
        profile.OwnedAvatarFrameIds.ToArray(),
        profile.Ur);

    private static ApiException Changed() =>
        new(StatusCodes.Status409Conflict, "PLAYER_DATA_CHANGED", "玩家数据已发生变化，请刷新后重试");
}

public sealed class PlayerValidationException(Dictionary<string, string[]> errors)
    : ApiException(
        StatusCodes.Status422UnprocessableEntity,
        "VALIDATION_ERROR",
        "玩家资料格式不正确"), IApiValidationException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
