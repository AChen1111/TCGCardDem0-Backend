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
