using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HeroTriggerEffectTests
{
    [Fact]
    public void VyonOffersItsSummonTriggerAndChoosesTheHeroToSendDuringResolution()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "27780618", "89943723", "89943723", "89943723", "89943723" }
                .Concat(Enumerable.Repeat("09411399", 35)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var vyon = duel.State.Cards.Single(card => card.DefinitionId == "27780618");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 0,
            CardId = vyon.InstanceId, Slot = 0 }).Accepted);
        Assert.Equal("trigger.offer", duel.State.PendingDecision.Continuation);
        Answer(duel, "yes");
        Assert.Single(duel.State.Chain);
        Pass(duel); Pass(duel);
        var option = duel.State.PendingDecision.Options[0];
        var selected = duel.State.Cards.Single(card => card.Ref.Equals(option.Card));
        Assert.Equal(DuelZone.Deck, selected.Zone);
        Answer(duel, option.Id);
        Assert.Equal(DuelZone.Graveyard, selected.Zone);
        Assert.Equal(DuelZone.Monster, vyon.Zone);
        Assert.Empty(duel.State.Chain);
    }

    [Fact]
    public void BlazemanSearchesPolymerizationAndShadowMistOffersItsGraveyardSearchAfterAnEffectSendsIt()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "63060238", "81439173", "89943723", "89943723", "89943723" }
                .Concat(new[] { "24094653", "50720316" }).Concat(Enumerable.Repeat("09411399", 33)).ToArray(),
                Enumerable.Repeat("89631139", 40).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var blaze = duel.State.Cards.Single(card => card.DefinitionId == "63060238");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 0, CardId = blaze.InstanceId }).Accepted);
        Answer(duel, "yes"); Pass(duel); Pass(duel);
        Answer(duel, duel.State.PendingDecision.Options.Single().Id);
        Assert.Equal(DuelZone.Hand, duel.State.Cards.Single(card => card.DefinitionId == "24094653").Zone);
        Pass(duel); Pass(duel);
        var foolish = duel.State.Cards.Single(card => card.DefinitionId == "81439173");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = foolish.InstanceId, AbilityId = "81439173.1" }).Accepted);
        Pass(duel); Pass(duel);
        var mist = duel.State.Cards.Single(card => card.DefinitionId == "50720316");
        Answer(duel, mist.InstanceId.ToString());
        Assert.Equal("trigger.offer", duel.State.PendingDecision.Continuation);
        Answer(duel, "yes"); Pass(duel); Pass(duel);
        Assert.DoesNotContain(duel.State.PendingDecision.Options, option => duel.State.Cards.Single(card => card.Ref.Equals(option.Card)).DefinitionId == "50720316");
        Answer(duel, duel.State.PendingDecision.Options[0].Id);
        Assert.Contains(duel.State.Cards, card => card.DefinitionId == "09411399" && card.Zone == DuelZone.Hand);
    }

    [Fact]
    public void InfernalRageSpecialSummonOffersFavoriteContactFromTheGraveyard()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "24094653", "89943723", "40044918", "75047173", "26077389" }
                .Concat(Enumerable.Repeat("89631139", 35)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "93347961" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var favorite = duel.State.Cards.Single(card => card.DefinitionId == "75047173");
        favorite.Zone = DuelZone.Graveyard; favorite.Position = CardPosition.FaceUp;
        var spell = duel.State.Cards.Single(card => card.DefinitionId == "24094653");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = spell.InstanceId, AbilityId = "24094653.1" }).Accepted);
        Pass(duel); Pass(duel); Answer(duel, duel.State.PendingDecision.Options[0].Id);
        Answer(duel, duel.State.PendingDecision.Options.Select(option => option.Id).ToArray());
        Answer(duel, "0"); Answer(duel, "attack");
        Answer(duel, "yes"); Pass(duel); Pass(duel);
        Answer(duel, duel.State.PendingDecision.Options.Single().Id);
        Assert.Equal(DuelZone.Hand, favorite.Zone);
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
