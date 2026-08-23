namespace AChen.Backend.Api.Features.GameConfig;

public sealed record AvatarConfigResponse(
    int Id,
    string Name,
    string ResourceKey,
    int SortOrder,
    bool IsEnabled);

public sealed record CardPackConfigResponse(
    int Id,
    string Title,
    string CoverResourceKey,
    long PriceGold,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int SortOrder,
    bool IsEnabled);

public sealed record GameConfigBootstrapResponse(
    int SchemaVersion,
    long Revision,
    DateTimeOffset PublishedAt,
    IReadOnlyList<AvatarConfigResponse> Avatars,
    IReadOnlyList<CardPackConfigResponse> CardPacks);

public sealed record AvatarDefinitionInput(
    int Id,
    string Name,
    string ResourceKey,
    int SortOrder,
    bool IsEnabled,
    long ExpectedEditRevision);

public sealed record CardPackDefinitionInput(
    int Id,
    string Title,
    string CoverResourceKey,
    long PriceGold,
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt,
    int SortOrder,
    bool IsEnabled,
    long ExpectedEditRevision);

public sealed record GameConfigAdminResponse(
    long DraftRevision,
    long EditRevision,
    long? PublishedRevision,
    DateTimeOffset? PublishedAt,
    IReadOnlyList<AvatarConfigResponse> Avatars,
    IReadOnlyList<CardPackConfigResponse> CardPacks);

public sealed record GameConfigPublicationResponse(
    long PublishedRevision,
    long DraftRevision,
    DateTimeOffset PublishedAt);

public sealed record GameConfigDraftData(
    IReadOnlyList<AvatarConfigResponse> Avatars,
    IReadOnlyList<CardPackConfigResponse> CardPacks);
