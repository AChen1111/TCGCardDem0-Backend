using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class DrawbreadEffectTests
{
    [Theory]
    [InlineData("40044918", false)]
    [InlineData("89631139", true)]
    public void DrawbreadPaysTwoHundredAndBranchesOnTheRevealedMonstersGraveyardAttribute(string drawnId, bool discard)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "83838727", "89943723", "26077389", "26077389", "26077389", drawnId }
                .Concat(Enumerable.Repeat("89631139", 34)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var neos = duel.State.Cards.Single(card => card.DefinitionId == "89943723");
        neos.Zone = DuelZone.Graveyard; neos.Position = CardPosition.FaceUp;
        var bread = duel.State.Cards.Single(card => card.DefinitionId == "83838727");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = bread.InstanceId, AbilityId = "83838727.1" }).Accepted);
        Assert.Equal(7800, duel.State.Players[0].LifePoints);
        Pass(duel); Pass(duel);
        if (discard)
        {
            var choice = duel.State.PendingDecision;
            Assert.NotNull(choice);
            Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = choice.Player,
                DecisionId = choice.Id, Options = new[] { choice.Options[0].Id } }).Accepted);
        }
        else Assert.Null(duel.State.PendingDecision);
        Assert.Equal(discard ? 3 : 5, duel.State.Cards.Count(card => card.Owner == 0 && card.Zone == DuelZone.Hand));
        Assert.Equal(7800, duel.State.Players[0].LifePoints);
    }
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
}
