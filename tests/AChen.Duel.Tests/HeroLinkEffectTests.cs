using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HeroLinkEffectTests
{
    [Fact]
    public void CrossCrusaderLinkSummonOffersATargetedDestinyHeroRevival()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "26077389", "40044918", "09411399", "89943723", "89943723" }
                .Concat(Enumerable.Repeat("89943723", 35)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "58004362" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var materials = duel.State.Cards.Where(card => card.Owner == 0 && (card.DefinitionId == "26077389" || card.DefinitionId == "40044918")).ToArray();
        for (int index = 0; index < materials.Length; index++)
        { materials[index].Zone = DuelZone.Monster; materials[index].Position = CardPosition.FaceUpAttack; materials[index].Slot = index; }
        var malicious = duel.State.Cards.Single(card => card.DefinitionId == "09411399");
        malicious.Zone = DuelZone.Graveyard; malicious.Position = CardPosition.FaceUp;
        var cross = duel.State.Cards.Single(card => card.DefinitionId == "58004362");
        var summon = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = cross.InstanceId, Cards = materials.Select(card => card.InstanceId).ToArray(), Slot = 5 });
        Assert.True(summon.Accepted, summon.Error);
        Answer(duel, "yes"); Answer(duel, malicious.InstanceId.ToString());
        Pass(duel); Pass(duel); Answer(duel, "0"); Answer(duel, "defense");
        Assert.Equal(DuelZone.Monster, malicious.Zone);
        Assert.Equal(CardPosition.FaceUpDefense, malicious.Position);
        Assert.Equal(DuelZone.ExtraMonster, cross.Zone);
    }

    [Fact]
    public void CrossCrusaderTributesADestinyHeroToSearchAHeroWithADifferentName()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "09411399", "89943723", "89943723", "89943723", "89943723" }
                .Concat(Enumerable.Repeat("09411399", 10)).Concat(Enumerable.Repeat("40044918", 25)).ToArray(),
                Enumerable.Repeat("89631139", 40).ToArray() }, ExtraDecks = new[] { new[] { "58004362" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var malicious = duel.State.Cards.First(card => card.DefinitionId == "09411399");
        malicious.Zone = DuelZone.Monster; malicious.Position = CardPosition.FaceUpAttack;
        var cross = duel.State.Cards.Single(card => card.DefinitionId == "58004362");
        cross.Zone = DuelZone.ExtraMonster; cross.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = cross.InstanceId, AbilityId = "58004362.2", Cards = new[] { malicious.InstanceId } }).Accepted);
        Assert.Equal(DuelZone.Graveyard, malicious.Zone);
        Pass(duel); Pass(duel);
        Assert.All(duel.State.PendingDecision.Options, option => Assert.Equal("40044918",
            duel.State.Cards.Single(card => card.Ref.Equals(option.Card)).DefinitionId));
        Answer(duel, duel.State.PendingDecision.Options[0].Id);
        Assert.Contains(duel.State.Cards, card => card.DefinitionId == "40044918" && card.Zone == DuelZone.Hand);
    }

    [Fact]
    public void InfernalDevicerRevealsAHeroFusionAndSearchesTwoDifferentNamedMaterials()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "27780618", "63060238", "26077389", "26077389", "26077389" }
                .Concat(new[] { "89943723", "17955766" }).Concat(Enumerable.Repeat("89631139", 33)).ToArray(),
                Enumerable.Repeat("89631139", 40).ToArray() }, ExtraDecks = new[] { new[] { "19324993", "55171412" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var materials = duel.State.Cards.Where(card => card.Owner == 0 && (card.DefinitionId == "27780618" || card.DefinitionId == "63060238")).ToArray();
        for (int index = 0; index < materials.Length; index++)
        { materials[index].Zone = DuelZone.Monster; materials[index].Position = CardPosition.FaceUpAttack; materials[index].Slot = index; }
        var devicer = duel.State.Cards.Single(card => card.DefinitionId == "19324993");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = devicer.InstanceId, Cards = materials.Select(card => card.InstanceId).ToArray(), Slot = 5 }).Accepted);
        Answer(duel, "yes"); Pass(duel); Pass(duel);
        Answer(duel, duel.State.PendingDecision.Options.Single().Id);
        Assert.Equal(2, duel.State.PendingDecision.Options.Count);
        Answer(duel, duel.State.PendingDecision.Options.Select(option => option.Id).ToArray());
        Assert.Equal(DuelZone.Hand, duel.State.Cards.Single(card => card.DefinitionId == "89943723").Zone);
        Assert.Equal(DuelZone.Hand, duel.State.Cards.Single(card => card.DefinitionId == "17955766").Zone);
    }

    [Fact]
    public void WonderDriverMandatoryTriggerTargetsAndSetsAGraveFusionSpellInTheChosenSlot()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "27780618", "24094653", "89943723", "26077389", "26077389" }
                .Concat(Enumerable.Repeat("09411399", 35)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "01948619" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var wonder = duel.State.Cards.Single(card => card.DefinitionId == "01948619");
        wonder.Zone = DuelZone.ExtraMonster; wonder.Position = CardPosition.FaceUpAttack;
        var poly = duel.State.Cards.Single(card => card.DefinitionId == "24094653");
        poly.Zone = DuelZone.Graveyard; poly.Position = CardPosition.FaceUp;
        var vyon = duel.State.Cards.Single(card => card.DefinitionId == "27780618");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 0, CardId = vyon.InstanceId, Slot = 1 }).Accepted);
        Assert.Equal("trigger.target", duel.State.PendingDecision.Continuation);
        Answer(duel, poly.InstanceId.ToString());
        if (duel.State.PendingDecision != null) Answer(duel, "no");
        Pass(duel); Pass(duel); Answer(duel, "3");
        Assert.Equal(DuelZone.SpellTrap, poly.Zone);
        Assert.Equal(3, poly.Slot);
        Assert.Equal(CardPosition.FaceDown, poly.Position);
        Assert.Equal(duel.State.Turn, poly.SetTurn);
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
