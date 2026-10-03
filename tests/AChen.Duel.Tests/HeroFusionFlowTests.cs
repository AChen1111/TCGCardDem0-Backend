using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HeroFusionFlowTests
{
    [Fact]
    public void PolymerizationUsesSelectedHandMaterialsAndResumesThroughZoneAndPositionChoices()
    {
        var duel = Create("24094653");
        var spell = duel.State.Cards.Single(card => card.DefinitionId == "24094653");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = spell.InstanceId, AbilityId = "24094653.1" }).Accepted);
        Pass(duel); Pass(duel);
        Answer(duel, duel.State.PendingDecision.Options[0].Id);
        var materials = duel.State.Cards.Where(card => card.Owner == 0 && card.Zone == DuelZone.Hand
            && (card.DefinitionId == "89943723" || card.DefinitionId == "40044918")).ToArray();
        Answer(duel, materials.Select(card => card.InstanceId.ToString()).ToArray());
        Answer(duel, "0");
        Answer(duel, "attack");
        var sunrise = duel.State.Cards.Single(card => card.DefinitionId == "22908820");
        Assert.Equal(DuelZone.Monster, sunrise.Zone);
        Assert.Equal(SummonMethod.Fusion, sunrise.SummonMethod);
        Assert.True(sunrise.ProperlySummoned);
        Assert.All(materials, material => Assert.Equal(DuelZone.Graveyard, material.Zone));
        Assert.Equal(DuelZone.Graveyard, spell.Zone);
    }

    [Fact]
    public void MiracleFusionBanishesFieldAndGraveMaterials()
    {
        var duel = Create("45906428");
        var spell = duel.State.Cards.Single(card => card.DefinitionId == "45906428");
        var neos = duel.State.Cards.Single(card => card.Owner == 0 && card.DefinitionId == "89943723");
        var stratos = duel.State.Cards.Single(card => card.Owner == 0 && card.DefinitionId == "40044918");
        neos.Zone = DuelZone.Graveyard; neos.Position = CardPosition.FaceUp;
        stratos.Zone = DuelZone.Monster; stratos.Slot = 0; stratos.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = spell.InstanceId, AbilityId = "45906428.1" }).Accepted);
        Pass(duel); Pass(duel);
        Answer(duel, duel.State.PendingDecision.Options[0].Id);
        Answer(duel, neos.InstanceId.ToString(), stratos.InstanceId.ToString());
        Answer(duel, "0"); Answer(duel, "defense");
        Assert.Equal(DuelZone.Banished, neos.Zone);
        Assert.Equal(DuelZone.Banished, stratos.Zone);
        Assert.Equal(CardPosition.FaceUpDefense, duel.State.Cards.Single(card => card.DefinitionId == "22908820").Position);
    }

    [Fact]
    public void MaskChangeUsesTheTargetsAttributeAndSpecialSummonsDarkLawWithItsOwnProcedure()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "21143940", "27780618", "89943723", "89943723", "89943723" }
                .Concat(Enumerable.Repeat("89631139", 35)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "58481572" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var spell = duel.State.Cards.Single(card => card.DefinitionId == "21143940");
        var vyon = duel.State.Cards.Single(card => card.DefinitionId == "27780618");
        vyon.Zone = DuelZone.Monster; vyon.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = spell.InstanceId, AbilityId = "21143940.1", TargetId = vyon.InstanceId }).Accepted);
        Pass(duel); Pass(duel);
        Assert.Equal(DuelZone.Graveyard, vyon.Zone);
        Answer(duel, duel.State.PendingDecision.Options.Single().Id);
        Answer(duel, "0"); Answer(duel, "attack");
        var darkLaw = duel.State.Cards.Single(card => card.DefinitionId == "58481572");
        Assert.Equal(DuelZone.Monster, darkLaw.Zone);
        Assert.Equal(SummonMethod.MaskChange, darkLaw.SummonMethod);
        Assert.True(darkLaw.ProperlySummoned);
    }

    [Fact]
    public void SunriseOffersItsSearchAfterFusionAndAddsMiracleFusionAtResolution()
    {
        var duel = Create("24094653", true);
        var spell = duel.State.Cards.Single(card => card.DefinitionId == "24094653");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = spell.InstanceId, AbilityId = "24094653.1" }).Accepted);
        Pass(duel); Pass(duel); Answer(duel, duel.State.PendingDecision.Options.Single().Id);
        var materials = duel.State.Cards.Where(card => card.Owner == 0 && card.Zone == DuelZone.Hand
            && (card.DefinitionId == "89943723" || card.DefinitionId == "40044918")).ToArray();
        Answer(duel, materials.Select(card => card.InstanceId.ToString()).ToArray());
        Answer(duel, "0"); Answer(duel, "attack");
        Assert.Equal("trigger.offer", duel.State.PendingDecision.Continuation);
        Answer(duel, "yes"); Pass(duel); Pass(duel); Answer(duel, duel.State.PendingDecision.Options.Single().Id);
        Assert.Equal(DuelZone.Hand, duel.State.Cards.Single(card => card.DefinitionId == "45906428").Zone);
    }

    [Fact]
    public void InfernalRageTributesItselfOnlyWithANormalFusionMaterialAndIgnoresTheChosenHerosSummonCondition()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "93347961", "22908820" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var rage = duel.State.Cards.Single(card => card.DefinitionId == "93347961");
        rage.Zone = DuelZone.Monster; rage.Position = CardPosition.FaceUpAttack; rage.SummonMethod = SummonMethod.Fusion;
        rage.ProperlySummoned = true;
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = rage.InstanceId, AbilityId = "93347961.2" }).Accepted);
        rage.SummonMaterialDefinitions.AddRange(new[] { "89943723", "40044918" });
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = rage.InstanceId, AbilityId = "93347961.2" }).Accepted);
        Assert.Equal(DuelZone.Graveyard, rage.Zone);
        Pass(duel); Pass(duel); Answer(duel, duel.State.PendingDecision.Options.Single().Id);
        Answer(duel, "0"); Answer(duel, "attack");
        var sunrise = duel.State.Cards.Single(card => card.DefinitionId == "22908820");
        Assert.Equal(DuelZone.Monster, sunrise.Zone);
        Assert.Equal(SummonMethod.Effect, sunrise.SummonMethod);
        Assert.False(sunrise.ProperlySummoned);
    }

    [Fact]
    public void WakeUpCanUseManyWarriorsThroughOneMaterialDecisionWithoutEnumeratingAllSubsets()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false, OpeningHand = 36,
            MainDecks = new[] { new[] { "24094653" }.Concat(Enumerable.Repeat("89943723", 39)).ToArray(),
                Enumerable.Repeat("89631139", 40).ToArray() }, ExtraDecks = new[] { new[] { "32828466", "22908820" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var sunrise = duel.State.Cards.Single(card => card.DefinitionId == "22908820");
        sunrise.Zone = DuelZone.Monster; sunrise.Position = CardPosition.FaceUpAttack;
        var spell = duel.State.Cards.Single(card => card.DefinitionId == "24094653");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = spell.InstanceId, AbilityId = "24094653.1" }).Accepted);
        Pass(duel); Pass(duel); Answer(duel, duel.State.PendingDecision.Options.Single().Id);
        Assert.Equal(36, duel.State.PendingDecision.Options.Count);
        Answer(duel, duel.State.PendingDecision.Options.Select(option => option.Id).ToArray());
        Answer(duel, "0"); Answer(duel, "attack");
        var wake = duel.State.Cards.Single(card => card.DefinitionId == "32828466");
        Assert.Equal(DuelZone.Monster, wake.Zone);
        Assert.Equal(36, wake.SummonMaterialDefinitions.Count);
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
    static DuelEngine Create(string spell, bool withMiracle = false)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord {
            Shuffle = false, MainDecks = new[] { new[] { spell, "89943723", "40044918", "26077389", "26077389" }
                .Concat(withMiracle ? new[] { "45906428" }.Concat(Enumerable.Repeat("89631139", 34))
                    : Enumerable.Repeat("89631139", 35)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "22908820" }, Array.Empty<string>() }
        });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        return duel;
    }
}
