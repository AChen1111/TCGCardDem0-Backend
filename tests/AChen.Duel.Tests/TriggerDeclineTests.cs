using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class TriggerDeclineTests
{
    [Fact]
    public void DecliningTheOptionalTriggerReturnsToFastResponseWithoutLeavingAnEmptyDecisionWindow()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord {
            Shuffle = false, OpeningHand = 1, MainDecks = new[] {
                new[] { "08240199", "79814787", "89631139" }, new[] { "89631139", "89631139" } } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon,
            Player = 0, CardId = 1 }).Accepted);
        var decision = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer,
            Player = 0, DecisionId = decision.Id, Options = new[] { "no" } }).Accepted);
        Assert.Null(duel.State.PendingDecision);
        Assert.Empty(duel.State.Chain);
        Assert.Equal(TimingWindow.FastResponse, duel.State.Window);
        Assert.Equal(0, duel.State.WaitingSeat);
        Pass(duel); Pass(duel);
        Assert.Equal(TimingWindow.Open, duel.State.Window);
        Assert.Equal(DuelPhase.Main1, duel.State.Phase);
    }

    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
}
