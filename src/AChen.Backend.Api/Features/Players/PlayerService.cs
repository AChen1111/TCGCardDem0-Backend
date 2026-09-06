using AChen.Backend.Api.Infrastructure;
using AChen.Backend.Api.Features.GameConfig;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.Players;

public sealed class PlayerService(
    IPlayerRepository repository,
    GameConfigService gameConfigService,
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

    public async Task<PlayerResponse> GrantAvatarAsync(
        Guid userId,
        int avatarId,
        CancellationToken cancellationToken)
    {
        if (avatarId < 0)
        {
            throw new PlayerValidationException(new Dictionary<string, string[]>
            {
                ["avatarId"] = ["头像 ID 不能为负数"]
            });
        }

        await EnsureAvatarAvailableAsync(avatarId, cancellationToken);
        var profile = await GetRequiredAsync(userId, cancellationToken);
        if (profile.OwnedAvatarIds.Contains(avatarId))
        {
            return ToResponse(profile);
        }

        profile.OwnedAvatarIds = profile.OwnedAvatarIds.Append(avatarId).OrderBy(value => value).ToList();
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
            "Granted avatar {AvatarId} to player {UserId} at revision {Revision}.",
            avatarId,
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
        var published = await gameConfigService.GetPublishedAsync(cancellationToken);
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
            "Player {UserId} purchased {CatalogType} {ItemId} for {PriceGold} gold at revision {Revision}.",
            userId,
            catalogType,
            request.ItemId,
            priceGold,
            profile.Revision);
        return ToResponse(profile);
    }

    private async Task EnsureAvatarAvailableAsync(int avatarId, CancellationToken cancellationToken)
    {
        if (!await gameConfigService.IsAvatarAvailableAsync(avatarId, cancellationToken))
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "AVATAR_NOT_AVAILABLE",
                "该头像不存在或尚未启用");
        }
    }

    private async Task EnsureWallpaperAvailableAsync(int wallpaperId, CancellationToken cancellationToken)
    {
        if (!await gameConfigService.IsWallpaperAvailableAsync(wallpaperId, cancellationToken))
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

    private static PlayerResponse ToResponse(PlayerProfile profile) => new(
        profile.UserId,
        profile.Nickname,
        profile.AvatarId,
        profile.OwnedAvatarIds.ToArray(),
        profile.BackgroundId,
        profile.OwnedBackgroundIds.ToArray(),
        profile.Gold,
        profile.Revision,
        profile.CreatedAt,
        profile.UpdatedAt);

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
