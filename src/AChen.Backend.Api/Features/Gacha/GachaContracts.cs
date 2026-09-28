namespace AChen.Backend.Api.Features.Gacha;

public static class GachaPoolKeys
{
    public const string AllCards = "CardAll";
}

public sealed record GachaDrawResult(string CardId, int Rarity, string SourcePool, string? ArtId = null);
