using AChen.Backend.Api.Features.Duels;
using AChen.Duel.Core;

namespace AChen.Backend.Api.Tests;

// Room transport tests isolate the support gate; gameplay acceptance must use production CardRuleCatalog.
internal static class DuelRoomFixtures
{
    public static DuelRoomService Create(TimeProvider clock) => new(clock, new TransportFixtureSupport());
    internal sealed class TransportFixtureSupport : IDuelRoomCardSupport
    {
        public IReadOnlyList<MissingCardRule> FindMissing(IEnumerable<string> cardIds) => Array.Empty<MissingCardRule>();
    }
    internal sealed class MissingFixtureSupport : IDuelRoomCardSupport
    {
        public IReadOnlyList<MissingCardRule> FindMissing(IEnumerable<string> cardIds) => new[] {
            new MissingCardRule(cardIds.First(), CardRuleRequirement.Missing("fixture.missing", CardRuleKind.Restriction)) };
    }
}
