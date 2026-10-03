using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class BlueEyesTriggerTests
{
    [Fact]
    public void NormalSummonedSageOffersAnOptionalSearchExcludingAnotherSage()
    {
        var engine = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "08240199", "79814787", "08240199" }, new[] { "89631139" } }, OpeningHand = 1 });
        while (engine.State.Phase != DuelPhase.Main1) Pass(engine);
        var sage = engine.State.Cards.First(c => c.DefinitionId == "08240199" && c.Zone == DuelZone.Hand);
        Assert.True(engine.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 0, CardId = sage.InstanceId }).Accepted);
        var offer = Assert.IsType<DuelDecision>(engine.State.PendingDecision);
        Assert.True(engine.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0, DecisionId = offer.Id, Options = new[] { "yes" } }).Accepted);
        Pass(engine); Pass(engine);
        var search = Assert.IsType<DuelDecision>(engine.State.PendingDecision);
        Assert.Single(search.Options);
        Assert.Equal("79814787", engine.State.Cards.Single(c => c.InstanceId == search.Options[0].Card.InstanceId).DefinitionId);
    }
    static void Pass(DuelEngine e) => Assert.True(e.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = e.State.WaitingSeat }).Accepted);
}
