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

        if (request.AvatarId != profile.AvatarId &&
            request.AvatarId is int avatarId &&
            !await gameConfigService.IsAvatarAvailableAsync(avatarId, cancellationToken))
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "AVATAR_NOT_AVAILABLE",
                "Avatar does not exist or is not enabled in the published game configuration.");
        }

        profile.Nickname = request.Nickname.Trim();
        profile.AvatarId = request.AvatarId;
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

    private async Task<PlayerProfile> GetRequiredAsync(Guid userId, CancellationToken cancellationToken) =>
        await repository.GetOrCreateAsync(userId, timeProvider.GetUtcNow(), cancellationToken) ??
        throw new ApiException(
            StatusCodes.Status401Unauthorized,
            "INVALID_ACCESS_TOKEN",
            "Access token is no longer valid.");

    private static PlayerResponse ToResponse(PlayerProfile profile) => new(
        profile.UserId,
        profile.Nickname,
        profile.AvatarId,
        profile.Gold,
        profile.Revision,
        profile.CreatedAt,
        profile.UpdatedAt);

    private static ApiException Changed() =>
        new(StatusCodes.Status409Conflict, "PLAYER_DATA_CHANGED", "Player data changed. Refresh and try again.");
}

public sealed class PlayerValidationException(Dictionary<string, string[]> errors)
    : ApiException(
        StatusCodes.Status422UnprocessableEntity,
        "VALIDATION_ERROR",
        "Request validation failed."), IApiValidationException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
