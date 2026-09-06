namespace AChen.Backend.Api.Features.GameConfig;

public sealed class WallpaperDefinition
{
    public long Revision { get; init; }
    public int Id { get; init; }
    public required string Name { get; set; }
    public required string ResourceKey { get; set; }
    public long PriceGold { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; }
    public GameConfigVersion Version { get; init; } = null!;
}
