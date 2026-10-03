using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class BasicActionTests
{
    [Fact]
    public void SettingMonsterDoesNotTriggerWonderDriverAsASuccessfulSummon()
    {
        var duel = Hand("27780618");
        var source = duel.State.Cards.Single();
        var driver = AddMonster(duel, "01948619", 0); driver.Zone = DuelZone.ExtraMonster;
        var spell = AddMonster(duel, "24094653", 0); spell.Zone = DuelZone.Graveyard;
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SetMonster, Player = 0,
            CardId = source.InstanceId, Slot = 1 });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(CardPosition.FaceDownDefense, source.Position);
        Assert.DoesNotContain(result.Events, e => e.Kind == DuelEventKind.Summoned);
        Assert.Null(duel.State.PendingDecision);
        Assert.Empty(duel.State.Chain);
    }

    [Fact]
    public void SettingSpellFromHandCreatesAHiddenFieldCardWithoutConsumingNormalSummon()
    {
        var duel = Hand("00213326");
        var source = duel.State.Cards.Single();
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SetSpellTrap, Player = 0,
            CardId = source.InstanceId, Slot = 3 });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(DuelZone.SpellTrap, source.Zone);
        Assert.Equal(3, source.Slot);
        Assert.Equal(CardPosition.FaceDown, source.Position);
        Assert.Equal(duel.State.Turn, source.SetTurn);
        Assert.Equal(0, duel.State.Players[0].NormalSummonsThisTurn);
        var moved = Assert.Single(result.Events.Where(e => e.Kind == DuelEventKind.Moved));
        Assert.Equal(1, moved.VisibleToMask);
        Assert.Equal(TimingWindow.FastResponse, duel.State.Window);
    }

    [Fact]
    public void ChangingBattlePositionOpensResponseAndCannotBeRepeatedDuringTheSameTurn()
    {
        var duel = Hand("00213326");
        var monster = AddMonster(duel, "26077389", 1);
        Assert.Contains(duel.QueryLegalActions(0), a => a.Kind == DuelCommandKind.ChangePosition && a.Card.Equals(monster.Ref));
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.ChangePosition, Player = 0,
            CardId = monster.InstanceId, Position = CardPosition.FaceUpDefense });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(CardPosition.FaceUpDefense, monster.Position);
        Assert.Contains(result.Events, e => e.Kind == DuelEventKind.PositionChanged && e.Card.Equals(monster.Ref));
        PassResponse(duel);
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.Kind == DuelCommandKind.ChangePosition && a.Card.Equals(monster.Ref));
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.ChangePosition, Player = 0,
            CardId = monster.InstanceId, Position = CardPosition.FaceUpAttack }).Accepted);
    }

    [Theory]
    [InlineData(DuelCommandKind.NormalSummon)]
    [InlineData(DuelCommandKind.SetMonster)]
    public void SpecialSummonOnlyMonsterCannotBeNormalSummonedOrSet(DuelCommandKind kind)
    {
        var duel = Hand("38517737");
        var source = duel.State.Cards.Single();
        var first = AddMonster(duel, "26077389", 1);
        var second = AddMonster(duel, "26077389", 2);
        var result = duel.Apply(new DuelCommand { Kind = kind, Player = 0, CardId = source.InstanceId,
            Slot = 0, Cards = new[] { first.InstanceId, second.InstanceId } });
        Assert.False(result.Accepted);
        Assert.Equal("CANNOT_NORMAL_SUMMON", result.Error);
        Assert.Equal(DuelZone.Hand, source.Zone);
        Assert.Equal(0, duel.State.Players[0].NormalSummonsThisTurn);
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.Card.Equals(source.Ref)
            && (a.Kind == DuelCommandKind.NormalSummon || a.Kind == DuelCommandKind.SetMonster));
    }

    [Fact]
    public void TributingAnXyzSendsItsAttachmentsToGraveAndResetsItsFieldModifiers()
    {
        var duel = Hand("89631139");
        var source = duel.State.Cards.Single();
        var xyz = AddMonster(duel, "01639384", 0);
        var second = AddMonster(duel, "26077389", 1);
        var attachment = AddMonster(duel, "26077389", 4);
        attachment.Zone = DuelZone.Material; attachment.HostInstanceId = xyz.InstanceId;
        xyz.Materials.Add(attachment.InstanceId); xyz.CurrentAtk = 17; xyz.Negated = true;
        xyz.ProperlySummoned = true;
        var oldRef = xyz.Ref;
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 0,
            CardId = source.InstanceId, Slot = 0, Cards = new[] { xyz.InstanceId, second.InstanceId } });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(DuelZone.Graveyard, attachment.Zone);
        Assert.Equal(0, attachment.HostInstanceId);
        Assert.Empty(xyz.Materials);
        Assert.Equal(duel.Catalog.Get(xyz.DefinitionId).Attack, xyz.CurrentAtk);
        Assert.False(xyz.Negated);
        Assert.True(xyz.ProperlySummoned);
        Assert.NotEqual(oldRef, xyz.Ref);
        var moved = Assert.Single(result.Events.Where(e => e.Kind == DuelEventKind.Moved && e.Card.InstanceId == xyz.InstanceId));
        Assert.Equal(MoveCause.Tribute, moved.Cause);
        Assert.Equal(17, moved.Before.Attack);
        Assert.Equal(DuelZone.Monster, moved.Before.Zone);
    }

    static DuelEngine Hand(string id)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { id }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        duel.State.Players[0].Deck.Clear();
        var card = duel.State.Cards.Single(); card.Zone = DuelZone.Hand;
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        return duel;
    }

    static DuelCardState AddMonster(DuelEngine duel, string id, int slot)
    {
        var definition = duel.Catalog.Get(id);
        var card = new DuelCardState { InstanceId = duel.State.NextInstanceId++, Generation = 1,
            DefinitionId = id, Owner = 0, Controller = 0, Zone = DuelZone.Monster, Slot = slot,
            Position = CardPosition.FaceUpAttack, CurrentAtk = definition.Attack, CurrentDef = definition.Defense,
            CurrentLevel = definition.Level, CurrentAttribute = definition.Attribute, CurrentRace = definition.Race,
            CurrentNameId = definition.OriginalNameId, ProperlySummoned = true };
        duel.State.Cards.Add(card); return card;
    }

    static void PassResponse(DuelEngine duel)
    {
        for (int i = 0; i < 30 && duel.State.Window != TimingWindow.Open; i++)
            Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
        Assert.Equal(TimingWindow.Open, duel.State.Window);
    }
}
