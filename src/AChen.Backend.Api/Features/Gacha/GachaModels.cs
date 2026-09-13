namespace AChen.Backend.Api.Features.Gacha;

public sealed class AllCard
{
    public required string CardId { get; set; }
    public required string SourcePool { get; set; }
}

public sealed class GachaPoolEntry
{
    public required string PoolKey { get; set; }
    public required string CardId { get; set; }
    public int Weight { get; set; }
}

public sealed class GachaRarityWeight
{
    public int Rarity { get; set; }
    public int Weight { get; set; }
}
