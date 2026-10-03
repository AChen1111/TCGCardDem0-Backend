using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class DecisionInvariantTests
{
    [Fact]
    public void RejectedDecisionAndRepeatedLegalQueriesLeaveTheEntireRuleStateUnchanged()
    {
        var record = new DuelStartRecord { Shuffle = false, MainDecks = new[] {
            new[] { "00213326", "26077389", "26077389", "26077389", "26077389" }.Concat(Enumerable.Repeat("40044918", 35)).ToArray(),
            Enumerable.Repeat("89631139", 40).ToArray() } };
        var engine = new DuelEngine(DuelCardCatalog.CreateDefault(), record);
        while (engine.State.Phase != DuelPhase.Main1) Pass(engine);
        engine.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = 1, AbilityId = "00213326.1" });
        Pass(engine); Pass(engine);
        var decision = Assert.IsType<DuelDecision>(engine.State.PendingDecision);
        string before = DuelStateDigest.Compute(engine.State);
        Assert.False(engine.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0,
            DecisionId = decision.Id, Options = new[] { "invalid-option" } }).Accepted);
        for (int i = 0; i < 10; i++) { engine.QueryLegalActions(0); engine.QueryLegalActions(1); }
        Assert.Equal(before, DuelStateDigest.Compute(engine.State));
    }
    static void Pass(DuelEngine engine) => Assert.True(engine.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = engine.State.WaitingSeat }).Accepted);
}
