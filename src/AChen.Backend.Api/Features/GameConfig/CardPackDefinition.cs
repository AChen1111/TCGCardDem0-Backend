namespace AChen.Backend.Api.Features.GameConfig;

public sealed class CardPackDefinition
{
    public long Revision { get; init; }
    public int Id { get; init; }
    public required string Title { get; set; }
    public required string CoverResourceKey { get; set; }
    public long PriceGold { get; set; }
    public DateTimeOffset? StartsAt { get; set; }
    public DateTimeOffset? EndsAt { get; set; }
    public int SortOrder { get; set; }
    public bool IsEnabled { get; set; }
    public GameConfigVersion Version { get; init; } = null!;
}
