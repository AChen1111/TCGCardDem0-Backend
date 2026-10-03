using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class PhaseWindowTests
{
    [Fact]
    public void PhaseEffectRemainsAnAvailableActionWhilePlayersAreStillInTheEndPhase()
    {
        // 自定义程序隔离公共阶段窗口，真实Shizuku/AquaNeos由逐卡用例覆盖。
        var ability = new PhaseTriggerProgramAbility("26077389", 99, new[] { DuelZone.Monster }, false, false,
            (c, f) => false, c => c.State.Phase == DuelPhase.End && c.State.TurnPlayer == c.Player,
            c => true, (c, l) => c.Recover(c.Player, 100));
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            OpeningHand = 1, MainDecks = new[] { new[] { "26077389", "89631139" }, new[] { "89631139", "89631139" } } },
            new DuelAbilityRegistry(new[] { ability }));
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 0, CardId = 1 }).Accepted);
        Pass(duel); Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        Pass(duel);
        Assert.Contains(duel.QueryLegalActions(0), a => a.AbilityId == ability.AbilityId);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = 1, AbilityId = ability.AbilityId }).Accepted);
        Pass(duel); Pass(duel);
        Assert.Equal(8100, duel.State.Players[0].LifePoints);
    }
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
}
