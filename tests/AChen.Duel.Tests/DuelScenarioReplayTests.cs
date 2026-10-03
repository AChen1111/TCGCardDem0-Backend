using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class DuelScenarioReplayTests
{
    [Fact]
    public void ConsonanceWhiteStoneAshAndCrossoutReplayAcrossCostsResponsesAndConsecutiveChoices()
    {
        var printed = DuelCardCatalog.CreateDefault();
        var rules = CardRuleCatalog.CreateDefault(printed);
        var start = DuelRulePackage.CreateDefault(printed).Freeze(new DuelStartRecord {
            Shuffle = false, Seed = 39, MainDecks = new[] {
                Deck(printed, rules, "39701395", "79814787", "65681983", "40044918", "26077389", "14558127",
                    "89631139", "89631139", "89631139"),
                Deck(printed, rules, "14558127", "89631139", "00213326", "40044918", "21143940") } });
        var duel = new DuelEngine(printed, start);
        var recorder = new DuelReplayRecorder(start, duel.State);
        var facts = new List<DuelEvent>();
        void Apply(DuelCommand command, bool accepted = true)
        {
            string before = DuelStateDigest.Compute(duel.State);
            duel.QueryLegalActions(0); duel.QueryLegalActions(1);
            Assert.Equal(before, DuelStateDigest.Compute(duel.State));
            var result = duel.Apply(command);
            Assert.Equal(accepted, result.Accepted);
            recorder.Append(command, result, DuelStateDigest.Compute(duel.State));
            facts.AddRange(result.Events);
        }
        void Pass() => Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat });
        while (duel.State.Phase != DuelPhase.Main1) Pass();
        Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = 1, AbilityId = "39701395.1", Cards = new[] { 2 } });
        Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1, CardId = 41, AbilityId = "14558127.1" });
        Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = 3, AbilityId = "65681983.1",
            Slot = 1, NameId = "14558127" });
        Pass(); Pass();
        var crossout = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal(3, duel.State.Chain.Count);
        Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0, DecisionId = crossout.Id, Options = new[] { "invalid" } }, false);
        Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0, DecisionId = crossout.Id, Options = new[] { crossout.Options.Single().Id } });
        Assert.Equal("79814787.1", Assert.Single(duel.State.Chain).AbilityId);
        Pass(); Pass();
        var legend = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.NotEqual(crossout.Id, legend.Id);
        var opponent = new SeatProjection(1).Project(duel.State, duel.QueryLegalActions(1));
        Assert.Null(opponent.Decision);
        Assert.DoesNotContain(opponent.Cards, c => c.Owner == 0 && c.Zone == DuelZone.Hand);
        Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0, DecisionId = crossout.Id, Options = new[] { crossout.Options[0].Id } }, false);
        Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0, DecisionId = legend.Id, Options = new[] { legend.Options[0].Id } });
        Assert.Empty(duel.State.Chain);
        Assert.Contains(facts, f => f.Kind == DuelEventKind.Negated && f.Detail == "14558127.1" && !f.ActivationNegated);
        Assert.Single(facts.Where(f => f.Kind == DuelEventKind.Moved && f.Card.InstanceId == 2 && f.Cause == MoveCause.Cost));
        Assert.Equal(DuelZone.Graveyard, duel.State.Cards.Single(c => c.InstanceId == 2).Zone);
        Assert.Equal(5, duel.State.Cards.Count(c => c.Owner == 0 && c.Zone == DuelZone.Hand));
        var replay = DuelReplayRunner.Run(printed, recorder.Capture());
        Assert.True(replay.Verified, replay.Error);
        Assert.Equal(DuelStateDigest.Compute(duel.State), replay.FinalStateHash);
    }

    static string[] Deck(DuelCardCatalog printed, CardRuleCatalog rules, params string[] first)
    {
        var deck = first.ToList();
        foreach (var card in printed.Cards.Where(c => !c.IsExtra && rules.Get(c.CardId).Support.IsComplete))
            while (deck.Count < 40 && deck.Count(id => printed.Get(id).OriginalNameId == card.OriginalNameId) < card.MaxCopies)
                deck.Add(card.CardId);
        Assert.Equal(40, deck.Count);
        Assert.Empty(rules.CheckDeckSupport(deck));
        return deck.ToArray();
    }
}
