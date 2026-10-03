using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class SpecialSummonTests
{
    [Fact]
    public void ContactSummonReturnsItsNamedMonstersToDeckWithoutBuildingAChain()
    {
        var duel = Board("55171412", "89943723", "17955766");
        var source = duel.State.Cards.Single(c => c.Zone == DuelZone.ExtraDeck);
        var materials = duel.State.Cards.Where(c => c.Zone == DuelZone.Monster).ToArray();
        materials[0].Position = CardPosition.FaceDownDefense;
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = source.InstanceId, Cards = materials.Select(c => c.InstanceId).ToArray(), Slot = 0 });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(SummonMethod.Contact, source.SummonMethod);
        Assert.True(source.ProperlySummoned);
        Assert.All(materials, c => Assert.Equal(DuelZone.Deck, c.Zone));
        Assert.Equal(2, duel.State.Players[0].Deck.Count);
        Assert.Empty(duel.State.Chain);
    }

    [Fact]
    public void LinkSummonCanUseZonePointedToByASurvivingLinkMonster()
    {
        var duel = Board("12421694", "26077389", "08491308");
        var material = duel.State.Cards.Single(c => c.DefinitionId == "26077389");
        var arrow = duel.State.Cards.Single(c => c.DefinitionId == "08491308");
        arrow.Zone = DuelZone.ExtraMonster; arrow.Slot = 0;
        var source = duel.State.Cards.Single(c => c.Zone == DuelZone.ExtraDeck);
        Assert.Contains(duel.QueryLegalActions(0), a => a.Card.Equals(source.Ref) && a.Slots.Contains(0));
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = source.InstanceId, Cards = new[] { material.InstanceId }, Slot = 0 });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(DuelZone.Monster, source.Zone);
        Assert.Equal(DuelZone.ExtraMonster, arrow.Zone);
    }

    [Fact]
    public void SynchroUsesOneTunerAndCorrectNonTunerLevelsThenOpensAResponseWindow()
    {
        var duel = Board("40908371", "79814787", "89631139");
        var materials = duel.State.Cards.Where(c => c.Zone == DuelZone.Monster).ToArray();
        var extra = duel.State.Cards.Single(c => c.Zone == DuelZone.ExtraDeck);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = extra.InstanceId, Cards = materials.Select(c => c.InstanceId).ToArray(), Slot = 3 });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(DuelZone.Monster, extra.Zone);
        Assert.Equal(3, extra.Slot);
        Assert.True(extra.ProperlySummoned);
        Assert.All(materials, c => Assert.Equal(DuelZone.Graveyard, c.Zone));
        Assert.Equal(TimingWindow.FastResponse, duel.State.Window);
    }

    [Theory]
    [InlineData("50954680", "89943723")]
    [InlineData("59822133", "55063751")]
    public void SynchroRejectsNonTunerThatDoesNotMatchItsPrintedMaterialRequirement(string synchro, string nonTuner)
    {
        var duel = Board(synchro, "79814787", nonTuner);
        var extra = duel.State.Cards.Single(c => c.Zone == DuelZone.ExtraDeck);
        var materials = duel.State.Cards.Where(c => c.Zone == DuelZone.Monster).ToArray();
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = extra.InstanceId, Cards = materials.Select(c => c.InstanceId).ToArray(), Slot = 2 }).Accepted);
        Assert.All(materials, c => Assert.Equal(DuelZone.Monster, c.Zone));
        Assert.DoesNotContain(duel.QueryLegalActions(0), x => x.Kind == DuelCommandKind.SpecialSummon);
    }

    [Fact]
    public void SynchroUsesCurrentLevelAndCannotUseFaceDownOrRepeatedMaterials()
    {
        var duel = Board("40908371", "79814787", "89631139");
        var cards = duel.State.Cards.Where(c => c.Zone == DuelZone.Monster).ToArray();
        var extra = duel.State.Cards.Single(c => c.Zone == DuelZone.ExtraDeck);
        var command = new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0, CardId = extra.InstanceId,
            Cards = cards.Select(c => c.InstanceId).ToArray(), Slot = 2 };
        cards[1].CurrentLevel = 7;
        Assert.False(duel.Apply(command).Accepted);
        cards[1].CurrentLevel = 8; cards[1].Position = CardPosition.FaceDownDefense;
        Assert.False(duel.Apply(command).Accepted);
        cards[1].Position = CardPosition.FaceUpAttack;
        command.Cards = new[] { cards[0].InstanceId, cards[0].InstanceId };
        Assert.False(duel.Apply(command).Accepted);
    }

    [Fact]
    public void XyzOverlaysEqualLevelMonstersInsteadOfSendingThemToTheGraveyard()
    {
        var duel = Board("01639384", "89631139", "89631139");
        var materials = duel.State.Cards.Where(c => c.Zone == DuelZone.Monster).ToArray();
        var extra = duel.State.Cards.Single(c => c.Zone == DuelZone.ExtraDeck);
        Assert.Contains(duel.QueryLegalActions(0), a => a.Kind == DuelCommandKind.SpecialSummon && a.Card.Equals(extra.Ref));
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = extra.InstanceId, Cards = materials.Select(c => c.InstanceId).ToArray(), Slot = 0 });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(materials.Select(c => c.InstanceId), extra.Materials);
        Assert.All(materials, c => { Assert.Equal(DuelZone.Material, c.Zone); Assert.Equal(extra.InstanceId, c.HostInstanceId); });
        Assert.DoesNotContain(result.Events, e => e.To == DuelZone.Graveyard && e.HasCard);
    }

    [Theory]
    [InlineData("02530830")]
    [InlineData("39030163")]
    public void GalaxyEyesAlternativeOverlayTransfersTheWholeExistingMaterialStack(string targetId)
    {
        var duel = Board(targetId, "18963306", "89631139");
        var original = duel.State.Cards.Single(c => c.DefinitionId == "18963306");
        var attached = duel.State.Cards.Single(c => c.DefinitionId == "89631139");
        attached.Zone = DuelZone.Material; attached.HostInstanceId = original.InstanceId;
        original.Materials.Add(attached.InstanceId);
        var target = duel.State.Cards.Single(c => c.Zone == DuelZone.ExtraDeck);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = target.InstanceId, Cards = new[] { original.InstanceId }, Slot = 0 });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(new[] { attached.InstanceId, original.InstanceId }.OrderBy(x => x), target.Materials.OrderBy(x => x));
        Assert.Equal(target.InstanceId, attached.HostInstanceId);
        Assert.Empty(original.Materials);
    }

    [Fact]
    public void CipherBladeCannotBecomeXyzMaterialEvenForAnAlternativeOverlay()
    {
        var duel = Board("39030163", "02530830");
        var source = duel.State.Cards.Single(c => c.Zone == DuelZone.Monster);
        var target = duel.State.Cards.Single(c => c.Zone == DuelZone.ExtraDeck);
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = target.InstanceId, Cards = new[] { source.InstanceId }, Slot = 0 }).Accepted);
        Assert.Equal(DuelZone.Monster, source.Zone);
    }

    [Fact]
    public void LinkSummonUsesAnExtraMonsterZoneAndHasNoDefensePosition()
    {
        var duel = Board("08491308", "26077389");
        var material = duel.State.Cards.Single(c => c.Zone == DuelZone.Monster);
        var extra = duel.State.Cards.Single(c => c.Zone == DuelZone.ExtraDeck);
        var command = new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = extra.InstanceId, Cards = new[] { material.InstanceId }, Slot = 5 };
        var action = Assert.Single(duel.QueryLegalActions(0).Where(a => a.Kind == DuelCommandKind.SpecialSummon));
        Assert.Equal(new[] { 5, 6 }, action.Slots);
        command.Position = CardPosition.FaceUpDefense;
        Assert.False(duel.Apply(command).Accepted);
        command.Position = CardPosition.FaceUpAttack;
        var result = duel.Apply(command);
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(DuelZone.ExtraMonster, extra.Zone);
        Assert.Equal(0, extra.Slot);
        Assert.Equal(DuelZone.Graveyard, material.Zone);
    }

    [Theory]
    [InlineData("01948619", "40044918", "40044918")]
    [InlineData("19324993", "40044918", "40044918")]
    [InlineData("58004362", "40044918", "40044918")]
    [InlineData("08491308", "26077389", "")]
    [InlineData("12421694", "26077389", "")]
    [InlineData("25072579", "26077389", "")]
    [InlineData("63288574", "26077389", "")]
    [InlineData("90673289", "26077389", "")]
    [InlineData("17217034", "26077389", "26077389")]
    [InlineData("98462037", "26077389", "26077389")]
    [InlineData("75147529", "26077389", "26077389")]
    [InlineData("76072561", "26077389", "26077389")]
    [InlineData("63013339", "26077389", "26077389")]
    [InlineData("56741506", "75147529", "26077389")]
    public void LinkProcedureEnforcesEachCatalogMaterialRecipe(string targetId, string first, string second)
    {
        var definition = DuelCardCatalog.CreateDefault().Get(targetId);
        string wrongId = targetId == "17217034" || targetId == "98462037" ? "14558127" : "89631139";
        var invalid = Board(targetId, Enumerable.Repeat(wrongId, definition.LinkRating).ToArray());
        Assert.False(SummonAll(invalid, 5).Accepted);
        var valid = Board(targetId, second == "" ? new[] { first } : new[] { first, second });
        var result = SummonAll(valid, 5);
        Assert.True(result.Accepted, result.Error);
    }

    static DuelStepResult SummonAll(DuelEngine duel, int slot) => duel.Apply(new DuelCommand
    {
        Kind = DuelCommandKind.SpecialSummon, Player = 0, Slot = slot,
        CardId = duel.State.Cards.Single(c => c.Zone == DuelZone.ExtraDeck).InstanceId,
        Cards = duel.State.Cards.Where(c => c.Zone == DuelZone.Monster).Select(c => c.InstanceId).ToArray()
    });

    static DuelEngine Board(string extraId, params string[] materials)
    {
        var start = new DuelStartRecord { MainDecks = new[] { materials, Array.Empty<string>() },
            ExtraDecks = new[] { new[] { extraId }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false };
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), start, new DuelAbilityRegistry(Array.Empty<IAbilityHandler>()));
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        int slot = 0;
        foreach (var card in duel.State.Cards.Where(c => c.Zone == DuelZone.Deck))
        {
            duel.State.Players[0].Deck.Remove(card.InstanceId);
            card.Zone = DuelZone.Monster; card.Slot = slot++; card.Position = CardPosition.FaceUpAttack;
        }
        return duel;
    }
}
