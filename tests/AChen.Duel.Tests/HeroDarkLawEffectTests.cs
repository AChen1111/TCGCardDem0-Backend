using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HeroDarkLawEffectTests
{
    [Fact]
    public void DarkLawReplacesTheOpponentsGraveyardMovesIncludingSpellCleanup()
    {
        var duel = Create("81439173");
        var foolish = duel.State.Cards.Single(card => card.DefinitionId == "81439173");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = foolish.InstanceId, AbilityId = "81439173.1" }).Accepted);
        Pass(duel); Pass(duel);
        var choice = duel.State.PendingDecision;
        var target = duel.State.Cards.Single(card => card.Ref.Equals(choice.Options[0].Card));
        Answer(duel, choice.Options[0].Id);
        Assert.Equal(DuelZone.Banished, target.Zone);
        Assert.Equal(DuelZone.Banished, foolish.Zone);
        Assert.Empty(duel.State.Cards.Where(card => card.Owner == 1 && card.Zone == DuelZone.Graveyard));
    }

    [Fact]
    public void DarkLawOffersAfterAnOpponentsEffectDrawAndBanishesOneRandomHandCard()
    {
        var duel = Create("70368879");
        var upstart = duel.State.Cards.Single(card => card.DefinitionId == "70368879");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = upstart.InstanceId, AbilityId = "70368879.1" }).Accepted);
        Pass(duel); Pass(duel);
        Assert.Equal("trigger.offer", duel.State.PendingDecision.Continuation);
        int hand = duel.State.Cards.Count(card => card.Owner == 1 && card.Zone == DuelZone.Hand);
        Answer(duel, "yes"); Pass(duel); Pass(duel);
        Assert.Equal(hand - 1, duel.State.Cards.Count(card => card.Owner == 1 && card.Zone == DuelZone.Hand));
    }
    static DuelEngine Create(string spell)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false, FirstPlayer = 1,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(),
                new[] { spell }.Concat(Enumerable.Repeat("89631139", 39)).ToArray() }, ExtraDecks = new[] { new[] { "58481572" }, Array.Empty<string>() } });
        var darkLaw = duel.State.Cards.Single(card => card.DefinitionId == "58481572");
        darkLaw.Zone = DuelZone.Monster; darkLaw.Position = CardPosition.FaceUpAttack;
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
