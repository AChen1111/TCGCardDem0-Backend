using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class SkyTemperanceEffectTests
{
    [Fact]
    public void TemperanceSpecialSummonPaysASpellBanishCostAndEquipsTheChosenOpponentMonster()
    {
        var duel = Create();
        var linkMaterial = duel.State.Cards.Single(card => card.DefinitionId == "01948619");
        linkMaterial.Zone = DuelZone.ExtraMonster; linkMaterial.Position = CardPosition.FaceUpAttack;
        var warrior = duel.State.Cards.First(card => card.Owner == 0 && card.DefinitionId == "26077389");
        warrior.Zone = DuelZone.Monster; warrior.Position = CardPosition.FaceUpAttack;
        var target = duel.State.Cards.Single(card => card.DefinitionId == "27780618");
        target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack;
        var temperance = duel.State.Cards.Single(card => card.DefinitionId == "56741506");
        var summon = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = temperance.InstanceId, Cards = new[] { linkMaterial.InstanceId, warrior.InstanceId }, Slot = 5 });
        Assert.True(summon.Accepted, summon.Error);
        Answer(duel, "yes");
        var spell = duel.State.Cards.Single(card => card.DefinitionId == "70368879");
        Answer(duel, spell.InstanceId.ToString()); Answer(duel, target.InstanceId.ToString());
        Assert.Equal(DuelZone.Banished, spell.Zone);
        Pass(duel); Pass(duel); Answer(duel, "2");
        Assert.Equal(DuelZone.SpellTrap, target.Zone);
        Assert.Equal(0, target.Controller);
        Assert.Equal(temperance.Ref, target.EquipTarget);
    }

    [Fact]
    public void TemperanceBattleDestructionCanSpecialSummonADifferentSkyStrikerFromHand()
    {
        var duel = Create(); duel.State.Turn = 2;
        var temperance = duel.State.Cards.Single(card => card.DefinitionId == "56741506");
        temperance.Zone = DuelZone.Monster; temperance.Position = CardPosition.FaceUpAttack;
        var opponent = duel.State.Cards.First(card => card.Owner == 1 && card.Zone == DuelZone.Hand && card.DefinitionId == "89631139");
        opponent.Zone = DuelZone.Monster; opponent.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.Battle }).Accepted); Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0, CardId = temperance.InstanceId, TargetId = opponent.InstanceId }).Accepted);
        for (int step = 0; duel.State.PendingDecision == null && step < 24; step++) Pass(duel);
        Assert.Equal(DuelZone.Graveyard, temperance.Zone);
        Answer(duel, "yes"); Pass(duel); Pass(duel);
        Assert.NotNull(duel.State.PendingDecision);
        int summoned = duel.State.PendingDecision.Options[0].Card.InstanceId;
        Answer(duel, summoned.ToString()); Answer(duel, "1"); Answer(duel, "attack");
        Assert.Equal(DuelZone.Monster, duel.State.Cards.Single(card => card.InstanceId == summoned).Zone);
        Assert.Equal("26077389", duel.State.Cards.Single(card => card.InstanceId == summoned).DefinitionId);
    }
    static DuelEngine Create()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "70368879" }.Concat(Enumerable.Repeat("26077389", 39)).ToArray(),
                new[] { "27780618" }.Concat(Enumerable.Repeat("89631139", 39)).ToArray() },
            ExtraDecks = new[] { new[] { "56741506", "01948619" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        return duel;
    }
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
    static void Answer(DuelEngine duel, params string[] options)
    {
        var choice = duel.State.PendingDecision;
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = choice.Player, DecisionId = choice.Id, Options = options });
        Assert.True(result.Accepted, result.Error);
    }
}
