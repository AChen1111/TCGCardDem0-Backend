using AChen.Backend.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.GameConfig;

public sealed class GameConfigService(
    IGameConfigRepository repository,
    TimeProvider timeProvider,
    ILogger<GameConfigService> logger)
{
    public async Task<GameConfigBootstrapResponse> GetPublishedAsync(CancellationToken cancellationToken)
    {
        var published = await repository.GetLatestPublishedAsync(true, cancellationToken) ??
            throw new ApiException(
                StatusCodes.Status404NotFound,
                "GAME_CONFIG_NOT_PUBLISHED",
                "Game configuration has not been published.");
        return ToBootstrapResponse(published);
    }

    public async Task<GameConfigAdminResponse> GetAdminAsync(CancellationToken cancellationToken)
    {
        var draft = await EnsureDraftAsync(cancellationToken);
        var published = await repository.GetLatestPublishedAsync(false, cancellationToken);
        return new GameConfigAdminResponse(
            draft.Revision,
            draft.EditRevision,
            published?.Revision,
            published?.PublishedAt,
            draft.Avatars.OrderBy(value => value.SortOrder).ThenBy(value => value.Id).Select(ToResponse).ToArray(),
            draft.CardPacks.OrderBy(value => value.SortOrder).ThenBy(value => value.Id).Select(ToResponse).ToArray());
    }

    public async Task<GameConfigDraftData> GetDraftDataAsync(CancellationToken cancellationToken)
    {
        var draft = await EnsureDraftAsync(cancellationToken);
        return new GameConfigDraftData(
            draft.Avatars.OrderBy(value => value.SortOrder).ThenBy(value => value.Id).Select(ToResponse).ToArray(),
            draft.CardPacks.OrderBy(value => value.SortOrder).ThenBy(value => value.Id).Select(ToResponse).ToArray());
    }

    public Task ReplaceDraftAsync(
        GameConfigDraftData imported,
        long expectedEditRevision,
        CancellationToken cancellationToken)
    {
        ValidateDraftData(imported);
        return repository.ExecuteInTransactionAsync(async () =>
        {
            var draft = await GetMatchingDraftAsync(expectedEditRevision, cancellationToken);
            var published = await repository.GetLatestPublishedAsync(true, cancellationToken);

            var avatarTargets = imported.Avatars.ToDictionary(value => value.Id);
            var cardPackTargets = imported.CardPacks.ToDictionary(value => value.Id);
            if (published is not null)
            {
                foreach (var avatar in published.Avatars.Where(value => !avatarTargets.ContainsKey(value.Id)))
                {
                    avatarTargets.Add(avatar.Id, ToResponse(avatar) with { IsEnabled = false });
                }

                foreach (var cardPack in published.CardPacks.Where(value => !cardPackTargets.ContainsKey(value.Id)))
                {
                    cardPackTargets.Add(cardPack.Id, ToResponse(cardPack) with { IsEnabled = false });
                }
            }

            ApplyAvatarTargets(draft, avatarTargets);
            ApplyCardPackTargets(draft, cardPackTargets);
            TouchDraft(draft);
            await SaveDraftAsync(cancellationToken);
            logger.LogInformation(
                "Replaced game configuration draft {DraftRevision} from an imported snapshot.",
                draft.Revision);
            return true;
        }, cancellationToken);
    }

    public async Task UpsertAvatarAsync(AvatarDefinitionInput input, CancellationToken cancellationToken)
    {
        ThrowIfInvalid(GameConfigValidation.Validate(input));
        var draft = await GetMatchingDraftAsync(input.ExpectedEditRevision, cancellationToken);
        var duplicateKey = draft.Avatars.FirstOrDefault(value =>
            value.Id != input.Id &&
            string.Equals(value.ResourceKey, input.ResourceKey.Trim(), StringComparison.OrdinalIgnoreCase));
        if (duplicateKey is not null)
        {
            throw new GameConfigValidationException(new Dictionary<string, string[]>
            {
                ["resourceKey"] = ["ResourceKey must be unique within the draft."]
            });
        }

        var avatar = draft.Avatars.SingleOrDefault(value => value.Id == input.Id);
        if (avatar is null)
        {
            draft.Avatars.Add(new AvatarDefinition
            {
                Revision = draft.Revision,
                Id = input.Id,
                Name = input.Name.Trim(),
                ResourceKey = input.ResourceKey.Trim(),
                SortOrder = input.SortOrder,
                IsEnabled = input.IsEnabled
            });
        }
        else
        {
            avatar.Name = input.Name.Trim();
            avatar.ResourceKey = input.ResourceKey.Trim();
            avatar.SortOrder = input.SortOrder;
            avatar.IsEnabled = input.IsEnabled;
        }

        TouchDraft(draft);
        await SaveDraftAsync(cancellationToken);
    }

    public async Task DeleteAvatarAsync(
        int id,
        long expectedEditRevision,
        CancellationToken cancellationToken)
    {
        var draft = await GetMatchingDraftAsync(expectedEditRevision, cancellationToken);
        var avatar = draft.Avatars.SingleOrDefault(value => value.Id == id) ??
            throw new ApiException(StatusCodes.Status404NotFound, "AVATAR_NOT_FOUND", "Avatar was not found.");
        if (await repository.WasAvatarPublishedAsync(id, cancellationToken))
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "PUBLISHED_CONFIG_ITEM_CANNOT_BE_DELETED",
                "Published avatars must be disabled instead of deleted.");
        }

        repository.RemoveAvatar(avatar);
        TouchDraft(draft);
        await SaveDraftAsync(cancellationToken);
    }

    public async Task UpsertCardPackAsync(CardPackDefinitionInput input, CancellationToken cancellationToken)
    {
        ThrowIfInvalid(GameConfigValidation.Validate(input));
        var draft = await GetMatchingDraftAsync(input.ExpectedEditRevision, cancellationToken);
        var cardPack = draft.CardPacks.SingleOrDefault(value => value.Id == input.Id);
        if (cardPack is null)
        {
            draft.CardPacks.Add(new CardPackDefinition
            {
                Revision = draft.Revision,
                Id = input.Id,
                Title = input.Title.Trim(),
                CoverResourceKey = input.CoverResourceKey.Trim(),
                PriceGold = input.PriceGold,
                StartsAt = input.StartsAt,
                EndsAt = input.EndsAt,
                SortOrder = input.SortOrder,
                IsEnabled = input.IsEnabled
            });
        }
        else
        {
            cardPack.Title = input.Title.Trim();
            cardPack.CoverResourceKey = input.CoverResourceKey.Trim();
            cardPack.PriceGold = input.PriceGold;
            cardPack.StartsAt = input.StartsAt;
            cardPack.EndsAt = input.EndsAt;
            cardPack.SortOrder = input.SortOrder;
            cardPack.IsEnabled = input.IsEnabled;
        }

        TouchDraft(draft);
        await SaveDraftAsync(cancellationToken);
    }

    public async Task DeleteCardPackAsync(
        int id,
        long expectedEditRevision,
        CancellationToken cancellationToken)
    {
        var draft = await GetMatchingDraftAsync(expectedEditRevision, cancellationToken);
        var cardPack = draft.CardPacks.SingleOrDefault(value => value.Id == id) ??
            throw new ApiException(StatusCodes.Status404NotFound, "CARD_PACK_NOT_FOUND", "Card pack was not found.");
        if (await repository.WasCardPackPublishedAsync(id, cancellationToken))
        {
            throw new ApiException(
                StatusCodes.Status422UnprocessableEntity,
                "PUBLISHED_CONFIG_ITEM_CANNOT_BE_DELETED",
                "Published card packs must be disabled instead of deleted.");
        }

        repository.RemoveCardPack(cardPack);
        TouchDraft(draft);
        await SaveDraftAsync(cancellationToken);
    }

    public async Task<GameConfigPublicationResponse> PublishAsync(
        long expectedEditRevision,
        CancellationToken cancellationToken) =>
        await repository.ExecuteInTransactionAsync(async () =>
        {
            var draft = await GetMatchingDraftAsync(expectedEditRevision, cancellationToken);
            var publishedAt = timeProvider.GetUtcNow();
            draft.State = GameConfigVersionState.Published;
            draft.EditRevision++;
            draft.UpdatedAt = publishedAt;
            draft.PublishedAt = publishedAt;
            await SaveDraftAsync(cancellationToken);

            var next = CloneAsDraft(draft, draft.Revision + 1, publishedAt);
            repository.AddVersion(next);
            await repository.SaveChangesAsync(cancellationToken);
            logger.LogInformation(
                "Published game configuration revision {PublishedRevision}; draft {DraftRevision} is now active for editing.",
                draft.Revision,
                next.Revision);
            return new GameConfigPublicationResponse(draft.Revision, next.Revision, publishedAt);
        }, cancellationToken);

    public Task<bool> IsAvatarAvailableAsync(int id, CancellationToken cancellationToken) =>
        repository.IsLatestPublishedAvatarEnabledAsync(id, cancellationToken);

    private async Task<GameConfigVersion> EnsureDraftAsync(CancellationToken cancellationToken)
    {
        var draft = await repository.GetDraftAsync(true, cancellationToken);
        if (draft is not null)
        {
            return draft;
        }

        var published = await repository.GetLatestPublishedAsync(true, cancellationToken);
        var now = timeProvider.GetUtcNow();
        draft = published is null
            ? NewDraft(1, now)
            : CloneAsDraft(published, published.Revision + 1, now);
        repository.AddVersion(draft);
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
            return draft;
        }
        catch (DbUpdateException)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "GAME_CONFIG_CHANGED",
                "Game configuration changed. Refresh and try again.");
        }
    }

    private async Task<GameConfigVersion> GetMatchingDraftAsync(
        long expectedEditRevision,
        CancellationToken cancellationToken)
    {
        var draft = await EnsureDraftAsync(cancellationToken);
        if (draft.EditRevision != expectedEditRevision)
        {
            throw Changed();
        }

        return draft;
    }

    private async Task SaveDraftAsync(CancellationToken cancellationToken)
    {
        try
        {
            await repository.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw Changed();
        }
    }

    private void TouchDraft(GameConfigVersion draft)
    {
        draft.EditRevision++;
        draft.UpdatedAt = timeProvider.GetUtcNow();
    }

    private static GameConfigVersion NewDraft(long revision, DateTimeOffset now) => new()
    {
        Revision = revision,
        State = GameConfigVersionState.Draft,
        EditRevision = 0,
        CreatedAt = now,
        UpdatedAt = now
    };

    private static GameConfigVersion CloneAsDraft(
        GameConfigVersion source,
        long revision,
        DateTimeOffset now)
    {
        var draft = NewDraft(revision, now);
        foreach (var avatar in source.Avatars)
        {
            draft.Avatars.Add(new AvatarDefinition
            {
                Revision = revision,
                Id = avatar.Id,
                Name = avatar.Name,
                ResourceKey = avatar.ResourceKey,
                SortOrder = avatar.SortOrder,
                IsEnabled = avatar.IsEnabled
            });
        }

        foreach (var cardPack in source.CardPacks)
        {
            draft.CardPacks.Add(new CardPackDefinition
            {
                Revision = revision,
                Id = cardPack.Id,
                Title = cardPack.Title,
                CoverResourceKey = cardPack.CoverResourceKey,
                PriceGold = cardPack.PriceGold,
                StartsAt = cardPack.StartsAt,
                EndsAt = cardPack.EndsAt,
                SortOrder = cardPack.SortOrder,
                IsEnabled = cardPack.IsEnabled
            });
        }

        return draft;
    }

    private static GameConfigBootstrapResponse ToBootstrapResponse(GameConfigVersion version) => new(
        1,
        version.Revision,
        version.PublishedAt ?? throw new InvalidOperationException("Published config is missing PublishedAt."),
        version.Avatars.OrderBy(value => value.SortOrder).ThenBy(value => value.Id).Select(ToResponse).ToArray(),
        version.CardPacks.OrderBy(value => value.SortOrder).ThenBy(value => value.Id).Select(ToResponse).ToArray());

    private static AvatarConfigResponse ToResponse(AvatarDefinition value) => new(
        value.Id,
        value.Name,
        value.ResourceKey,
        value.SortOrder,
        value.IsEnabled);

    private static CardPackConfigResponse ToResponse(CardPackDefinition value) => new(
        value.Id,
        value.Title,
        value.CoverResourceKey,
        value.PriceGold,
        value.StartsAt,
        value.EndsAt,
        value.SortOrder,
        value.IsEnabled);

    private static void ThrowIfInvalid(Dictionary<string, string[]> errors)
    {
        if (errors.Count > 0)
        {
            throw new GameConfigValidationException(errors);
        }
    }

    private static void ValidateDraftData(GameConfigDraftData imported)
    {
        if (imported.Avatars.Count + imported.CardPacks.Count > 10_000)
        {
            throw new GameConfigValidationException(new Dictionary<string, string[]>
            {
                ["file"] = ["A configuration snapshot cannot contain more than 10000 items."]
            });
        }

        if (imported.Avatars.GroupBy(value => value.Id).Any(group => group.Count() > 1))
        {
            throw new GameConfigValidationException(new Dictionary<string, string[]>
            {
                ["avatars"] = ["Avatar IDs must be unique."]
            });
        }

        if (imported.CardPacks.GroupBy(value => value.Id).Any(group => group.Count() > 1))
        {
            throw new GameConfigValidationException(new Dictionary<string, string[]>
            {
                ["cardPacks"] = ["Card pack IDs must be unique."]
            });
        }

        if (imported.Avatars
            .GroupBy(value => value.ResourceKey, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            throw new GameConfigValidationException(new Dictionary<string, string[]>
            {
                ["avatars"] = ["Avatar resource keys must be unique."]
            });
        }

        var errors = new Dictionary<string, string[]>();
        foreach (var avatar in imported.Avatars)
        {
            foreach (var error in GameConfigValidation.Validate(new AvatarDefinitionInput(
                         avatar.Id,
                         avatar.Name,
                         avatar.ResourceKey,
                         avatar.SortOrder,
                         avatar.IsEnabled,
                         0)))
            {
                errors[$"Avatar {avatar.Id}: {error.Key}"] = error.Value;
            }
        }

        foreach (var cardPack in imported.CardPacks)
        {
            foreach (var error in GameConfigValidation.Validate(new CardPackDefinitionInput(
                         cardPack.Id,
                         cardPack.Title,
                         cardPack.CoverResourceKey,
                         cardPack.PriceGold,
                         cardPack.StartsAt,
                         cardPack.EndsAt,
                         cardPack.SortOrder,
                         cardPack.IsEnabled,
                         0)))
            {
                errors[$"CardPack {cardPack.Id}: {error.Key}"] = error.Value;
            }
        }

        ThrowIfInvalid(errors);
    }

    private void ApplyAvatarTargets(
        GameConfigVersion draft,
        IReadOnlyDictionary<int, AvatarConfigResponse> targets)
    {
        foreach (var avatar in draft.Avatars.Where(value => !targets.ContainsKey(value.Id)).ToArray())
        {
            repository.RemoveAvatar(avatar);
        }

        foreach (var target in targets.Values)
        {
            var avatar = draft.Avatars.SingleOrDefault(value => value.Id == target.Id);
            if (avatar is null)
            {
                draft.Avatars.Add(new AvatarDefinition
                {
                    Revision = draft.Revision,
                    Id = target.Id,
                    Name = target.Name,
                    ResourceKey = target.ResourceKey,
                    SortOrder = target.SortOrder,
                    IsEnabled = target.IsEnabled
                });
                continue;
            }

            avatar.Name = target.Name;
            avatar.ResourceKey = target.ResourceKey;
            avatar.SortOrder = target.SortOrder;
            avatar.IsEnabled = target.IsEnabled;
        }
    }

    private void ApplyCardPackTargets(
        GameConfigVersion draft,
        IReadOnlyDictionary<int, CardPackConfigResponse> targets)
    {
        foreach (var cardPack in draft.CardPacks.Where(value => !targets.ContainsKey(value.Id)).ToArray())
        {
            repository.RemoveCardPack(cardPack);
        }

        foreach (var target in targets.Values)
        {
            var cardPack = draft.CardPacks.SingleOrDefault(value => value.Id == target.Id);
            if (cardPack is null)
            {
                draft.CardPacks.Add(new CardPackDefinition
                {
                    Revision = draft.Revision,
                    Id = target.Id,
                    Title = target.Title,
                    CoverResourceKey = target.CoverResourceKey,
                    PriceGold = target.PriceGold,
                    StartsAt = target.StartsAt,
                    EndsAt = target.EndsAt,
                    SortOrder = target.SortOrder,
                    IsEnabled = target.IsEnabled
                });
                continue;
            }

            cardPack.Title = target.Title;
            cardPack.CoverResourceKey = target.CoverResourceKey;
            cardPack.PriceGold = target.PriceGold;
            cardPack.StartsAt = target.StartsAt;
            cardPack.EndsAt = target.EndsAt;
            cardPack.SortOrder = target.SortOrder;
            cardPack.IsEnabled = target.IsEnabled;
        }
    }

    private static ApiException Changed() => new(
        StatusCodes.Status409Conflict,
        "GAME_CONFIG_CHANGED",
        "Game configuration changed. Refresh and try again.");
}

public sealed class GameConfigValidationException(Dictionary<string, string[]> errors)
    : ApiException(
        StatusCodes.Status422UnprocessableEntity,
        "VALIDATION_ERROR",
        "Request validation failed."), IApiValidationException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}
