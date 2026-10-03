using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class FieldNegationLifetimeTests
{
    [Fact]
    public void RayePreviouslyNegatedByVeilerCannotEscapeTheNegationByTributingHerselfAsCost()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false, OpeningHand = 0,
            MainDecks = new[] { new[] { "26077389", "89631139" }, new[] { "97268402", "89631139" } },
            ExtraDecks = new[] { new[] { "63288574" }, Array.Empty<string>() } });
        var raye = duel.State.Cards.Single(c => c.DefinitionId == "26077389");
        var veiler = duel.State.Cards.Single(c => c.DefinitionId == "97268402");
        // 初始局面：对方主阶段可响应的已表侧Raye与手牌Veiler。
        duel.State.Players[0].Deck.Remove(raye.InstanceId); raye.Zone = DuelZone.Monster; raye.Position = CardPosition.FaceUpAttack;
        duel.State.Players[1].Deck.Remove(veiler.InstanceId); veiler.Zone = DuelZone.Hand;
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.FastResponse; duel.State.WaitingSeat = 1;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = veiler.InstanceId, AbilityId = "97268402.1", TargetId = raye.InstanceId }).Accepted);
        Pass(duel); Pass(duel);
        Assert.True(raye.Negated);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = raye.InstanceId, AbilityId = "26077389.1" }).Accepted);
        Pass(duel); Pass(duel);
        Assert.Null(duel.State.PendingDecision);
        Assert.Equal(DuelZone.Graveyard, raye.Zone);
        Assert.DoesNotContain(duel.State.Cards, c => c.Zone == DuelZone.ExtraMonster);
    }
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
}
