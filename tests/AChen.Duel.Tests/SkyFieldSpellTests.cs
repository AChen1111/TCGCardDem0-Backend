using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class SkyFieldSpellTests
{
    [Fact]
    public void MultiroleCanSetAreaZeroInTheFieldZoneAndItIsBanishedWhenDestroyedLater()
    {
        var duel = Create("24010609", "50005218", "60461804", "26077389", "89631139");
        Place(duel, 1, DuelZone.SpellTrap); Place(duel, 2, DuelZone.Graveyard); Place(duel, 3, DuelZone.Monster);
        Place(duel, 6, DuelZone.Monster);
        // 初始局面包含本回合已完成的闪刀魔法发动历史。
        duel.State.TurnFacts.Add(new DuelEvent { Kind = DuelEventKind.Activated, Player = 0, DefinitionId = "63166096",
            IsCardActivation = true, ActivationKind = RuleCardKind.Spell, ChainId = 99, LinkNumber = 1,
            PresentCards = new() { new CardLastKnown { Ref = duel.State.Cards[0].Ref, Zone = DuelZone.SpellTrap, Position = CardPosition.FaceUp } } });
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        Pass(duel); Answer(duel, "yes"); Pass(duel); Pass(duel);
        Answer(duel, "2"); Answer(duel, "5");
        Assert.Equal(DuelZone.Field, duel.State.Cards.Single(c => c.InstanceId == 2).Zone);
        while (duel.State.WaitingSeat != 0) Pass(duel);
        Activate(duel, 3, "60461804.2"); Pass(duel); Pass(duel);
        Answer(duel, "2"); Answer(duel, "6");
        Assert.Equal(DuelZone.Banished, duel.State.Cards.Single(c => c.InstanceId == 2).Zone);
        Assert.DoesNotContain(duel.State.PendingTriggers, t => t.AbilityId == "50005218.2");
    }

    [Fact]
    public void MultiroleSetsDistinctNamesAtEndAfterAllZoneChoicesWithoutPublishingTheMappings()
    {
        var duel = Create("24010609", "63166096", "98338152", "26077389", "89631139", "89943723");
        Place(duel, 1, DuelZone.SpellTrap); Place(duel, 2, DuelZone.Hand); Place(duel, 3, DuelZone.Hand);
        Place(duel, 7, DuelZone.Monster);
        Activate(duel, 2, "63166096.1", slot: 1); Pass(duel); Pass(duel); Answer(duel, "4");
        Pass(duel); Pass(duel);
        Activate(duel, 3, "98338152.1", 7, 1); Pass(duel); Pass(duel); Pass(duel); Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        Pass(duel);
        Assert.Equal("24010609.2", duel.State.ActiveTrigger?.AbilityId);
        Answer(duel, "yes"); Pass(duel); Pass(duel);
        Answer(duel, "2", "3");
        Assert.Equal(DecisionKind.ChooseZone, duel.State.PendingDecision?.Kind);
        Answer(duel, "1");
        Assert.All(duel.State.Cards.Where(c => c.InstanceId is 2 or 3), c => Assert.Equal(DuelZone.Graveyard, c.Zone));
        var decision = duel.State.PendingDecision!;
        var final = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0, DecisionId = decision.Id, Options = new[] { "2" } });
        Assert.True(final.Accepted);
        Assert.All(duel.State.Cards.Where(c => c.InstanceId is 2 or 3), c => Assert.Equal(CardPosition.FaceDown, c.Position));
        var projection = new SeatProjection(1);
        var other = projection.Project(duel.State, duel.QueryLegalActions(1));
        Assert.All(other.Cards.Where(c => c.Owner == 0 && c.Zone == DuelZone.SpellTrap && c.Position == CardPosition.FaceDown), c => Assert.Equal("", c.DefinitionId));
        Assert.All(projection.ProjectEvents(final.Events).Where(e => e.Kind == DuelEventKind.Moved && e.To == DuelZone.SpellTrap),
            e => { Assert.Equal("", e.DefinitionId); Assert.Equal("", e.ViewCardId); });
    }

    [Fact]
    public void MultiroleDoesNotPreventResponsesToAnAlreadyFaceUpSpellsEffect()
    {
        var duel = Create("24010609", "26077389", "50005218", "63166096", "89631139", "89943723");
        Place(duel, 1, DuelZone.SpellTrap); Place(duel, 2, DuelZone.Monster); Place(duel, 3, DuelZone.Field);
        Place(duel, 7, DuelZone.Hand);
        Activate(duel, 1, "24010609.1", 2); Pass(duel); Pass(duel); Pass(duel); Pass(duel);
        Activate(duel, 3, "50005218.1", 1);
        Assert.Contains(duel.QueryLegalActions(1), a => a.AbilityId == "14558127.1");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = 7, AbilityId = "14558127.1" }).Accepted);
    }

    [Fact]
    public void MultiroleSendsItsOtherCardAndPreventsOpponentResponsesToLaterSpellCardActivations()
    {
        var duel = Create("24010609", "26077389", "70368879", "89631139");
        Place(duel, 1, DuelZone.SpellTrap); Place(duel, 2, DuelZone.Monster); Place(duel, 3, DuelZone.Hand);
        Place(duel, 5, DuelZone.Hand);
        Activate(duel, 1, "24010609.1", 2); Pass(duel); Pass(duel);
        Assert.Equal(DuelZone.Graveyard, duel.State.Cards.Single(c => c.InstanceId == 2).Zone);
        Pass(duel); Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = 3, AbilityId = "70368879.1", Slot = 1 }).Accepted);
        Assert.DoesNotContain(duel.QueryLegalActions(1), a => a.AbilityId == "14558127.1");
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = 5, AbilityId = "14558127.1" }).Accepted);
    }

    [Theory]
    [InlineData("50005218", DuelZone.Field)]
    [InlineData("24010609", DuelZone.SpellTrap)]
    public void PersistentSpellCanBeActivatedFromHandWithNoOtherCardOnField(string definitionId, DuelZone expectedZone)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            OpeningHand = 1, MainDecks = new[] { new[] { definitionId, "89631139" }, new[] { "89631139", "89631139" } } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        Activate(duel, 1, definitionId + ".0"); Pass(duel); Pass(duel);
        Assert.Equal(expectedZone, duel.State.Cards.Single(c => c.InstanceId == 1).Zone);
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.AbilityId == definitionId + ".0");
    }

    [Fact]
    public void AreaZeroSentFromFieldByPhoenixEffectOffersADeckSummonWithZoneAndPositionChoices()
    {
        var duel = Create("50005218", "60461804", "26077389", "37351133");
        Place(duel, 1, DuelZone.Field); Place(duel, 2, DuelZone.Monster);
        Place(duel, 5, DuelZone.Monster);
        Activate(duel, 2, "60461804.2"); Pass(duel); Pass(duel);
        Answer(duel, "1"); Answer(duel, "5");
        Assert.Equal("50005218.2", duel.State.ActiveTrigger?.AbilityId);
        Answer(duel, "yes"); Pass(duel); Pass(duel);
        Answer(duel, "3");
        Assert.Equal(DecisionKind.ChooseZone, duel.State.PendingDecision?.Kind);
        Answer(duel, "1");
        Assert.Equal(DecisionKind.ChoosePosition, duel.State.PendingDecision?.Kind);
        Answer(duel, "attack");
        var raye = duel.State.Cards.Single(c => c.InstanceId == 3);
        Assert.Equal(DuelZone.Monster, raye.Zone);
        Assert.Equal(1, raye.Slot);
        Assert.Equal(CardPosition.FaceUpAttack, raye.Position);
    }

    [Fact]
    public void AreaZeroSendsItsTargetEvenWhenTheRevealedStrikerCardIsNotAdded()
    {
        var duel = Create("50005218", "26077389", "63166096", "89631139", "89943723");
        Place(duel, 1, DuelZone.Field); Place(duel, 2, DuelZone.Monster);
        Activate(duel, 1, "50005218.1", 2);
        Pass(duel); Pass(duel);
        var choice = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal(0, choice.Min);
        Answer(duel);
        Assert.Equal(DuelZone.Graveyard, duel.State.Cards.Single(c => c.InstanceId == 2).Zone);
        Assert.Equal(DuelZone.Deck, duel.State.Cards.Single(c => c.InstanceId == 3).Zone);
        Assert.All(duel.State.Cards, c => Assert.Equal(0, c.RevealedToMask));
    }

    static DuelEngine Create(params string[] cards)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false, OpeningHand = 0,
            MainDecks = new[] { cards, new[] { "14558127", "89631139", "89631139" } } });
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open; duel.State.WaitingSeat = 0;
        return duel;
    }
    static void Place(DuelEngine duel, int id, DuelZone zone, int slot = 0)
    {
        // 仅用于建立初始局面；后续推进全部使用正式命令。
        var card = duel.State.Cards.Single(c => c.InstanceId == id);
        duel.State.Players[card.Owner].Deck.Remove(id); card.Zone = zone; card.Slot = slot;
        card.Position = zone == DuelZone.Monster ? CardPosition.FaceUpAttack : CardPosition.FaceUp;
    }
    static void Activate(DuelEngine duel, int card, string ability, int target = 0, int slot = 0) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Activate, Player = 0, CardId = card, AbilityId = ability, TargetId = target, Slot = slot }).Accepted);
    static void Answer(DuelEngine duel, params string[] options) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Answer, Player = duel.State.PendingDecision!.Player,
        DecisionId = duel.State.PendingDecision.Id, Options = options }).Accepted);
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
}
