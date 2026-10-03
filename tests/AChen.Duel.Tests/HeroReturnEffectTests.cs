using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HeroReturnEffectTests
{
    [Fact]
    public void FavoriteContactOrdersTheMaterialsAtDeckBottomAndSummonsWithoutFusionHistory()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "75047173", "89943723", "17955766", "26077389", "26077389" }
                .Concat(Enumerable.Repeat("89631139", 35)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "55171412" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        duel.State.Turn = 2;
        var trap = duel.State.Cards.Single(card => card.DefinitionId == "75047173");
        trap.Zone = DuelZone.SpellTrap; trap.Position = CardPosition.FaceDown; trap.SetTurn = 1;
        var neos = duel.State.Cards.Single(card => card.DefinitionId == "89943723");
        var dolphin = duel.State.Cards.Single(card => card.DefinitionId == "17955766");
        neos.Zone = DuelZone.Graveyard; neos.Position = CardPosition.FaceUp;
        dolphin.Zone = DuelZone.Banished; dolphin.Position = CardPosition.FaceUp;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = trap.InstanceId, AbilityId = "75047173.1" }).Accepted);
        Pass(duel); Pass(duel); Answer(duel, duel.State.PendingDecision.Options.Single().Id);
        Answer(duel, neos.InstanceId.ToString(), dolphin.InstanceId.ToString());
        Assert.Equal(DecisionKind.OrderCards, duel.State.PendingDecision.Kind);
        Answer(duel, dolphin.InstanceId.ToString(), neos.InstanceId.ToString());
        Answer(duel, "0"); Answer(duel, "attack");
        Assert.Equal(new[] { dolphin.InstanceId, neos.InstanceId }, duel.State.Players[0].Deck.TakeLast(2));
        var aqua = duel.State.Cards.Single(card => card.DefinitionId == "55171412");
        Assert.Equal(DuelZone.Monster, aqua.Zone);
        Assert.False(aqua.ProperlySummoned);
        Assert.Contains(duel.State.Effects, effect => effect.Kind == EffectRecordKind.CannotReturnToExtra && effect.Target.Equals(aqua.Ref));
    }

    [Fact]
    public void ENShuffleReturnsAFieldElementalHeroAndSummonsADifferentNameFromTheDeck()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "10186633", "89943723", "26077389", "26077389", "26077389" }
                .Concat(Enumerable.Repeat("09411399", 34)).Concat(new[] { "40044918" }).ToArray(),
                Enumerable.Repeat("89631139", 40).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var neos = duel.State.Cards.Single(card => card.DefinitionId == "89943723");
        neos.Zone = DuelZone.Monster; neos.Position = CardPosition.FaceUpAttack;
        var shuffle = duel.State.Cards.Single(card => card.DefinitionId == "10186633");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = shuffle.InstanceId, AbilityId = "10186633.1" }).Accepted);
        Pass(duel); Pass(duel); Answer(duel, neos.InstanceId.ToString());
        Assert.Equal(DuelZone.Deck, neos.Zone);
        Assert.All(duel.State.PendingDecision.Options, option => Assert.Equal("40044918", duel.State.Cards.Single(card => card.Ref.Equals(option.Card)).DefinitionId));
        Answer(duel, duel.State.PendingDecision.Options.Single().Id); Answer(duel, "0"); Answer(duel, "attack");
        Assert.Equal(DuelZone.Monster, duel.State.Cards.Single(card => card.DefinitionId == "40044918").Zone);
    }

    [Fact]
    public void ENShuffleGraveEffectBanishesItselfAndCanReturnNeosAloneBeforeDrawing()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "10186633", "89943723", "26077389", "26077389", "26077389" }
                .Concat(Enumerable.Repeat("89631139", 35)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var shuffle = duel.State.Cards.Single(card => card.DefinitionId == "10186633");
        shuffle.Zone = DuelZone.Graveyard; shuffle.Position = CardPosition.FaceUp;
        var neos = duel.State.Cards.Single(card => card.DefinitionId == "89943723");
        neos.Zone = DuelZone.Graveyard; neos.Position = CardPosition.FaceUp;
        int hand = duel.State.Cards.Count(card => card.Owner == 0 && card.Zone == DuelZone.Hand);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = shuffle.InstanceId, AbilityId = "10186633.2" }).Accepted);
        Assert.Equal(DuelZone.Banished, shuffle.Zone);
        Pass(duel); Pass(duel); Answer(duel, neos.InstanceId.ToString());
        Assert.Equal(hand + 1, duel.State.Cards.Count(card => card.Owner == 0 && card.Zone == DuelZone.Hand));
    }

    [Fact]
    public void AquaNeosReturnsToExtraDeckWhenItsEndPhaseMandatoryEffectResolves()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "55171412" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var aqua = duel.State.Cards.Single(card => card.DefinitionId == "55171412");
        aqua.Zone = DuelZone.Monster; aqua.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        for (int step = 0; step < 12 && aqua.Zone != DuelZone.ExtraDeck; step++) Pass(duel);
        Assert.Equal(DuelZone.ExtraDeck, aqua.Zone);
    }
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
    static void Answer(DuelEngine duel, params string[] options)
    {
        var choice = duel.State.PendingDecision;
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = choice.Player, DecisionId = choice.Id, Options = options });
        Assert.True(result.Accepted, result.Error);
    }
}
