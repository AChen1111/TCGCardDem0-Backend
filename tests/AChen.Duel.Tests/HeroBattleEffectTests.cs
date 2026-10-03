using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HeroBattleEffectTests
{
    [Fact]
    public void WakeMandatoryAfterCalculationDestroysTheBattleMonsterAndDealsItsOriginalAttack()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "32828466" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        duel.State.Turn = 2;
        var wake = duel.State.Cards.Single(card => card.DefinitionId == "32828466");
        wake.Zone = DuelZone.Monster; wake.Position = CardPosition.FaceUpAttack;
        var opponent = duel.State.Cards.First(card => card.Owner == 1 && card.Zone == DuelZone.Hand);
        opponent.Zone = DuelZone.Monster; opponent.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.Battle }).Accepted);
        Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0, CardId = wake.InstanceId, TargetId = opponent.InstanceId }).Accepted);
        for (int step = 0; opponent.Zone != DuelZone.Graveyard && step < 24; step++) Pass(duel);
        Assert.Equal(DuelZone.Graveyard, opponent.Zone);
        Assert.Equal(5000, duel.State.Players[1].LifePoints);
        Assert.Equal(7500, duel.State.Players[0].LifePoints);
        // Wake也被战斗标记破坏，后续仍须送墓；无融合召唤历史，不触发③。
        for (int step = 0; wake.Zone != DuelZone.Graveyard && step < 12; step++) Pass(duel);
        Assert.Equal(DuelZone.Graveyard, wake.Zone);
    }

    [Fact]
    public void ShiningNeosWingmanMandatoryBattleDestructionDealsTheVictimsPrintedAttack()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "56733747" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        duel.State.Turn = 2;
        var shining = duel.State.Cards.Single(card => card.DefinitionId == "56733747");
        shining.Zone = DuelZone.Monster; shining.Position = CardPosition.FaceUpAttack;
        var opponent = duel.State.Cards.First(card => card.Owner == 1 && card.Zone == DuelZone.Hand);
        opponent.Zone = DuelZone.Monster; opponent.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.Battle }).Accepted);
        Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0, CardId = shining.InstanceId, TargetId = opponent.InstanceId }).Accepted);
        for (int step = 0; duel.State.Players[1].LifePoints != 4900 && step < 32; step++) Pass(duel);
        Assert.Equal(4900, duel.State.Players[1].LifePoints);
        Assert.Equal(DuelZone.Graveyard, opponent.Zone);
    }

    [Fact]
    public void SunriseRespondsToAnotherFriendlyHerosBattleAndTargetsAFieldCard()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "22908820" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        duel.State.Turn = 2;
        var sunrise = duel.State.Cards.Single(card => card.DefinitionId == "22908820");
        sunrise.Zone = DuelZone.Monster; sunrise.Position = CardPosition.FaceUpAttack; sunrise.Slot = 1;
        var neos = duel.State.Cards.First(card => card.Owner == 0 && card.Zone == DuelZone.Hand);
        neos.Zone = DuelZone.Monster; neos.Position = CardPosition.FaceUpAttack;
        var opponent = duel.State.Cards.First(card => card.Owner == 1 && card.Zone == DuelZone.Hand);
        opponent.Zone = DuelZone.Monster; opponent.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.Battle }).Accepted); Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0, CardId = neos.InstanceId, TargetId = opponent.InstanceId }).Accepted);
        Answer(duel, "yes"); Answer(duel, opponent.InstanceId.ToString()); Pass(duel); Pass(duel);
        Assert.Equal(DuelZone.Graveyard, opponent.Zone);
        Assert.Equal(DuelZone.Monster, sunrise.Zone);
    }
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
    static void Answer(DuelEngine duel, params string[] options)
    {
        var choice = duel.State.PendingDecision;
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = choice.Player, DecisionId = choice.Id, Options = options });
        Assert.True(result.Accepted, result.Error);
    }
}
