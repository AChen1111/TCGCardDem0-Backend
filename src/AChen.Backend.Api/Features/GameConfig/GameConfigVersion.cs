namespace AChen.Backend.Api.Features.GameConfig;

public enum GameConfigVersionState
{
    Draft,
    Published
}

public sealed class GameConfigVersion
{
    public long Revision { get; init; }
    public GameConfigVersionState State { get; set; }
    public long EditRevision { get; set; }
    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? PublishedAt { get; set; }
    public List<AvatarDefinition> Avatars { get; } = [];
    public List<CardPackDefinition> CardPacks { get; } = [];
}
