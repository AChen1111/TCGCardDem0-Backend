using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class TriggerTimingTests
{
    [Fact]
    public void WhiteStoneSentAsCostWaitsUntilTheCurrentChainFinishes()
    {
        var record = new DuelStartRecord { Shuffle = false, MainDecks = new[] {
            new[] { "39701395", "79814787", "26077389", "26077389", "26077389" }
                .Concat(Enumerable.Repeat("89631139", 35)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() } };
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), record);
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = 1,
            AbilityId = "39701395.1", Cards = new[] { 2 } }).Accepted);
        Assert.Single(duel.State.Chain);
        Assert.Equal("39701395.1", duel.State.Chain[0].AbilityId);
        Pass(duel); Pass(duel);
        Assert.Single(duel.State.Chain);
        Assert.Equal("79814787.1", duel.State.Chain[0].AbilityId);
        Assert.Equal(5, duel.State.Cards.Count(c => c.Owner == 0 && c.Zone == DuelZone.Hand));
        Pass(duel); Pass(duel);
        var decision = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0,
            DecisionId = decision.Id, Options = new[] { decision.Options[0].Id } }).Accepted);
        Assert.Equal(6, duel.State.Cards.Count(c => c.Owner == 0 && c.Zone == DuelZone.Hand));
        Assert.Empty(duel.State.Chain);
    }

    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
}
