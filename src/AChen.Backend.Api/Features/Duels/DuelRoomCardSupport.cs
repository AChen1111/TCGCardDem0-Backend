using AChen.Backend.Api.Infrastructure;
using AChen.Duel.Core;

namespace AChen.Backend.Api.Features.Duels;

public interface IDuelRoomCardSupport
{
    IReadOnlyList<MissingCardRule> FindMissing(IEnumerable<string> cardIds);
}

public sealed class DuelRoomCardSupport(CardRuleCatalog rules) : IDuelRoomCardSupport
{
    public IReadOnlyList<MissingCardRule> FindMissing(IEnumerable<string> cardIds) => rules.CheckDeckSupport(cardIds);
}

public sealed class UnsupportedDuelCardsException : ApiException, IApiValidationException
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }
    public UnsupportedDuelCardsException(IReadOnlyList<MissingCardRule> missing)
        : base(422, "UNSUPPORTED_DUEL_CARDS", "构筑含尚未完整实现的卡牌规则")
    {
        Errors = missing.GroupBy(rule => rule.CardId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(rule => rule.RuleId).ToArray(), StringComparer.Ordinal);
    }
}
