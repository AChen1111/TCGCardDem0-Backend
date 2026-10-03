using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class TemporaryEffectTests
{
    [Fact]
    public void VeilerNegatesItsTargetOnlyUntilTheEndOfThatTurn()
    {
        var record = new DuelStartRecord { Shuffle = false, MainDecks = new[] {
            Enumerable.Repeat("26077389", 40).ToArray(), Enumerable.Repeat("97268402", 40).ToArray() } };
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), record);
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var target = duel.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 0, CardId = target.InstanceId }).Accepted);
        Pass(duel);
        var veiler = duel.State.Cards.First(c => c.Owner == 1 && c.Zone == DuelZone.Hand);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1, CardId = veiler.InstanceId,
            AbilityId = "97268402.1", TargetId = target.InstanceId }).Accepted);
        Pass(duel); Pass(duel);
        Assert.True(target.Negated);
        Assert.Equal(DuelZone.Graveyard, veiler.Zone);
        for (int i = 0; i < 20 && duel.State.Turn == 1; i++) Pass(duel);
        Assert.Equal(2, duel.State.Turn);
        Assert.False(target.Negated);
    }

    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
}
