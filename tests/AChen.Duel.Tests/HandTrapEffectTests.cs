using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HandTrapEffectTests
{
    [Fact]
    public void AshDiscardsItselfAndNegatesEmergencyCallsEffectWithoutOpeningASearchChoice()
    {
        var duel = Create(new[] { "00213326", "26077389", "26077389", "26077389", "26077389" },
            new[] { "14558127", "89631139", "89631139", "89631139", "89631139" });
        var spell = duel.State.Cards.Single(card => card.DefinitionId == "00213326");
        var ash = duel.State.Cards.Single(card => card.DefinitionId == "14558127");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = spell.InstanceId, AbilityId = "00213326.1" }).Accepted);
        Assert.Contains(duel.QueryLegalActions(1), action => action.AbilityId == "14558127.1");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = ash.InstanceId, AbilityId = "14558127.1" }).Accepted);
        Assert.Equal(DuelZone.Graveyard, ash.Zone);
        Assert.Equal(2, duel.State.Chain.Count);
        PassTwice(duel);
        Assert.Empty(duel.State.Chain);
        Assert.Null(duel.State.PendingDecision);
        Assert.Equal(35, duel.State.Players[0].Deck.Count);
        Assert.Equal(DuelZone.Graveyard, spell.Zone);
    }

    [Fact]
    public void DesiresCannotActivateAnotherCopyInTheSameTurn()
    {
        var duel = Create(new[] { "35261759", "35261759", "26077389", "26077389", "26077389" },
            Enumerable.Repeat("89631139", 5).ToArray());
        var pots = duel.State.Cards.Where(card => card.DefinitionId == "35261759").ToArray();
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = pots[0].InstanceId, AbilityId = "35261759.1" }).Accepted);
        PassTwice(duel);
        PassTwice(duel);
        Assert.Equal(TimingWindow.Open, duel.State.Window);
        Assert.DoesNotContain(duel.QueryLegalActions(0), action => action.AbilityId == "35261759.1");
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = pots[1].InstanceId, AbilityId = "35261759.1" }).Accepted);
    }

    static void PassTwice(DuelEngine duel)
    {
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
    }

    static DuelEngine Create(string[] hand0, string[] hand1)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord {
            Shuffle = false, MainDecks = new[] { hand0.Concat(Enumerable.Repeat("40044918", 35)).ToArray(),
                hand1.Concat(Enumerable.Repeat("89631139", 35)).ToArray() }
        });
        while (duel.State.Phase != DuelPhase.Main1)
            duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat });
        return duel;
    }
}
