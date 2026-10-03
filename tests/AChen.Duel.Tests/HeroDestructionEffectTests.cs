using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HeroDestructionEffectTests
{
    [Fact]
    public void PhoenixEnforcerDestroysItsOwnCardAndAnotherCardThenSchedulesANextStandbyRevival()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "09411399", "89943723", "89943723", "89943723", "89943723" }
                .Concat(Enumerable.Repeat("89631139", 35)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "60461804" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var phoenix = duel.State.Cards.Single(card => card.DefinitionId == "60461804");
        phoenix.Zone = DuelZone.Monster; phoenix.Position = CardPosition.FaceUpAttack; phoenix.ProperlySummoned = true;
        var opponent = duel.State.Cards.First(card => card.Owner == 1 && card.Zone == DuelZone.Hand);
        opponent.Zone = DuelZone.Monster; opponent.Position = CardPosition.FaceUpAttack;
        var malicious = duel.State.Cards.Single(card => card.DefinitionId == "09411399");
        malicious.Zone = DuelZone.Graveyard; malicious.Position = CardPosition.FaceUp;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = phoenix.InstanceId, AbilityId = "60461804.2" }).Accepted);
        Pass(duel); Pass(duel); Answer(duel, phoenix.InstanceId.ToString()); Answer(duel, opponent.InstanceId.ToString());
        Assert.Equal(DuelZone.Graveyard, phoenix.Zone);
        Assert.Equal(DuelZone.Graveyard, opponent.Zone);
        Assert.Equal("trigger.offer", duel.State.PendingDecision.Continuation);
        Answer(duel, "yes"); Pass(duel); Pass(duel);
        for (int step = 0; duel.State.PendingDecision == null && step < 30; step++)
        {
            var command = duel.State.Window == TimingWindow.Open && duel.State.Phase == DuelPhase.Main1
                ? new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = duel.State.WaitingSeat, Phase = DuelPhase.End }
                : new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat };
            var result = duel.Apply(command); Assert.True(result.Accepted, result.Error);
        }
        Assert.Equal(2, duel.State.Turn);
        Assert.Equal(DuelPhase.Standby, duel.State.Phase);
        Answer(duel, malicious.InstanceId.ToString()); Answer(duel, "0"); Answer(duel, ((int)CardPosition.FaceUpAttack).ToString());
        Assert.Equal(DuelZone.Monster, malicious.Zone);
        Assert.Equal(0, malicious.Controller);
    }

    [Fact]
    public void WonderDriverDestroyedByTheOpponentOffersAHeroSpecialSummonFromHand()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false, FirstPlayer = 1,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "01948619" }, new[] { "60461804" } } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var wonder = duel.State.Cards.Single(card => card.DefinitionId == "01948619");
        wonder.Zone = DuelZone.ExtraMonster; wonder.Position = CardPosition.FaceUpAttack;
        var phoenix = duel.State.Cards.Single(card => card.DefinitionId == "60461804");
        phoenix.Zone = DuelZone.Monster; phoenix.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = phoenix.InstanceId, AbilityId = "60461804.2" }).Accepted);
        Pass(duel); Pass(duel); Answer(duel, phoenix.InstanceId.ToString()); Answer(duel, wonder.InstanceId.ToString());
        Answer(duel, "no"); Answer(duel, "yes"); Pass(duel); Pass(duel);
        var selected = duel.State.PendingDecision.Options[0].Card.InstanceId;
        Answer(duel, selected.ToString()); Answer(duel, "1"); Answer(duel, "defense");
        Assert.Equal(DuelZone.Monster, duel.State.Cards.Single(card => card.InstanceId == selected).Zone);
        Assert.Equal(DuelZone.Graveyard, wonder.Zone);
    }

    [Fact]
    public void FusionSummonedWakeDestroyedByAnEffectMustSpecialSummonAWarrior()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "32828466", "60461804" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var wake = duel.State.Cards.Single(card => card.DefinitionId == "32828466");
        wake.Zone = DuelZone.Monster; wake.Position = CardPosition.FaceUpAttack; wake.Slot = 1;
        wake.ProperlySummoned = true; wake.SummonMethod = SummonMethod.Fusion;
        var phoenix = duel.State.Cards.Single(card => card.DefinitionId == "60461804");
        phoenix.Zone = DuelZone.Monster; phoenix.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = phoenix.InstanceId, AbilityId = "60461804.2" }).Accepted);
        Pass(duel); Pass(duel); Answer(duel, phoenix.InstanceId.ToString()); Answer(duel, wake.InstanceId.ToString());
        Assert.Contains(duel.State.Chain, link => link.AbilityId == "32828466.3");
        Answer(duel, "no"); Pass(duel); Pass(duel);
        int selected = duel.State.PendingDecision.Options[0].Card.InstanceId;
        Answer(duel, selected.ToString()); Answer(duel, "0"); Answer(duel, "attack");
        Assert.Equal(DuelZone.Monster, duel.State.Cards.Single(card => card.InstanceId == selected).Zone);
        Assert.Equal(DuelZone.Graveyard, wake.Zone);
    }

    [Fact]
    public void ShiningNeosWingmanGainsGraveyardMonsterAttackAndSurvivesAnEffectDestructionGroup()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false, FirstPlayer = 1,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "56733747" }, new[] { "60461804" } } });
        foreach (var hero in duel.State.Cards.Where(card => card.Owner == 0 && card.Zone == DuelZone.Hand).Take(2))
        { hero.Zone = DuelZone.Graveyard; hero.Position = CardPosition.FaceUp; }
        var shining = duel.State.Cards.Single(card => card.DefinitionId == "56733747");
        shining.Zone = DuelZone.Monster; shining.Position = CardPosition.FaceUpAttack;
        var phoenix = duel.State.Cards.Single(card => card.DefinitionId == "60461804");
        phoenix.Zone = DuelZone.Monster; phoenix.Position = CardPosition.FaceUpAttack;
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        Assert.Equal(3700, shining.CurrentAtk);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = phoenix.InstanceId, AbilityId = "60461804.2" }).Accepted);
        Pass(duel); Pass(duel); Answer(duel, phoenix.InstanceId.ToString()); Answer(duel, shining.InstanceId.ToString());
        Assert.Equal(DuelZone.Monster, shining.Zone);
        Assert.Equal(DuelZone.Graveyard, phoenix.Zone);
    }

    [Fact]
    public void ShiningNeosWingmanSpecialSummonOffersDestructionUpToTheFieldAttributeCount()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "24094653", "89943723", "26077389", "26077389", "26077389" }
                .Concat(Enumerable.Repeat("89631139", 35)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "56733747", "93347961" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var rage = duel.State.Cards.Single(card => card.DefinitionId == "93347961");
        rage.Zone = DuelZone.Monster; rage.Position = CardPosition.FaceUpAttack;
        var opponent = duel.State.Cards.First(card => card.Owner == 1 && card.Zone == DuelZone.Hand);
        opponent.Zone = DuelZone.Monster; opponent.Position = CardPosition.FaceUpAttack;
        var spell = duel.State.Cards.Single(card => card.DefinitionId == "24094653");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = spell.InstanceId, AbilityId = "24094653.1" }).Accepted);
        Pass(duel); Pass(duel); Answer(duel, duel.State.PendingDecision.Options.Single().Id);
        Answer(duel, duel.State.PendingDecision.Options.Select(option => option.Id).ToArray()); Answer(duel, "0"); Answer(duel, "attack");
        Assert.Equal("trigger.offer", duel.State.PendingDecision.Continuation);
        Answer(duel, "yes"); Pass(duel); Pass(duel);
        Assert.Equal(1, duel.State.PendingDecision.Max);
        Answer(duel, opponent.InstanceId.ToString());
        Assert.Equal(DuelZone.Graveyard, opponent.Zone);
    }
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
    static void Answer(DuelEngine duel, params string[] options)
    {
        var choice = duel.State.PendingDecision;
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = choice.Player,
            DecisionId = choice.Id, Options = options });
        Assert.True(result.Accepted, result.Error);
    }
}
