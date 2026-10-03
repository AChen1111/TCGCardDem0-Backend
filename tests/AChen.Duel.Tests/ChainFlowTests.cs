using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class ChainFlowTests
{
    static DuelEngine Create()
    {
        var record = new DuelStartRecord { Shuffle = false, MainDecks = new[]
        {
            new[] { "00213326", "26077389", "26077389", "26077389", "26077389" }
                .Concat(Enumerable.Repeat("40044918", 35)).ToArray(),
            Enumerable.Repeat("89631139", 40).ToArray()
        }};
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), record);
        while (duel.State.Phase != DuelPhase.Main1) duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat });
        return duel;
    }

    [Fact]
    public void SearchChoosesDuringResolutionAndResumesTheSameChainLink()
    {
        var duel = Create();
        var spell = duel.State.Cards.Single(x => x.DefinitionId == "00213326");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = spell.InstanceId, AbilityId = "00213326.1", Slot = 0 }).Accepted);
        Assert.Single(duel.State.Chain);
        Assert.Null(duel.State.PendingDecision);
        Assert.Equal(1, duel.State.WaitingSeat);
        duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 1 });
        duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 });
        var choice = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal(0, choice.Player);
        Assert.Single(duel.State.Chain);
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 1,
            DecisionId = choice.Id, Options = new[] { choice.Options[0].Id } }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0,
            DecisionId = choice.Id, Options = new[] { choice.Options[0].Id } }).Accepted);
        Assert.Null(duel.State.PendingDecision);
        Assert.Empty(duel.State.Chain);
        Assert.Equal(DuelZone.Graveyard, spell.Zone);
        Assert.Contains(duel.State.Cards, x => x.DefinitionId == "40044918" && x.Zone == DuelZone.Hand);
        Assert.Equal(34, duel.State.Players[0].Deck.Count);
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0,
            DecisionId = choice.Id, Options = new[] { choice.Options[0].Id } }).Accepted);
    }

    [Fact]
    public void AshBlossomSharesItsOncePerTurnLimitBetweenDifferentCopies()
    {
        var start = new DuelStartRecord { Shuffle = false, MainDecks = new[] {
            new[] { "00213326", "00213326", "26077389", "26077389", "26077389" }
                .Concat(Enumerable.Repeat("40044918", 35)).ToArray(), Enumerable.Repeat("14558127", 40).ToArray() } };
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), start);
        while (duel.State.Phase != DuelPhase.Main1) duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat });
        var spells = duel.State.Cards.Where(c => c.DefinitionId == "00213326").ToArray();
        var ashes = duel.State.Cards.Where(c => c.Owner == 1 && c.Zone == DuelZone.Hand).ToArray();
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = spells[0].InstanceId, AbilityId = "00213326.1" }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1, CardId = ashes[0].InstanceId, AbilityId = "14558127.1" }).Accepted);
        for (int i = 0; i < 4; i++) duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat });
        Assert.Equal(DuelZone.Graveyard, ashes[0].Zone);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = spells[1].InstanceId, AbilityId = "00213326.1" }).Accepted);
        Assert.DoesNotContain(duel.QueryLegalActions(1), a => a.AbilityId == "14558127.1");
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = ashes[1].InstanceId, AbilityId = "14558127.1" }).Accepted);
    }
}
