using AChen.Backend.Api.Features.Auth;

namespace AChen.Backend.Api.Features.Decks;

public sealed class PlayerDeck
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public required string Name { get; set; }
    public string MainDeckJson { get; set; } = "[]";
    public string ExtraDeckJson { get; set; } = "[]";
    public long Revision { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public User User { get; set; } = null!;
}

public sealed record DeckCardEntry(string CardId, int Rarity, int Count);
public sealed record CreateDeckRequest(string Name);
public sealed record SaveDeckRequest(string Name, DeckCardEntry[] MainDeck, DeckCardEntry[] ExtraDeck, long? ExpectedRevision);
public sealed record DeckResponse(Guid Id, string Name, IReadOnlyList<DeckCardEntry> MainDeck,
    IReadOnlyList<DeckCardEntry> ExtraDeck, long Revision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
