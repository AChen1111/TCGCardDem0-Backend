using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class ReplayTests
{
    [Fact]
    public void Digest_retains_private_trigger_facts_and_partial_trigger_costs()
    {
        var state = new DuelState { ActiveTrigger = new PendingTrigger { AbilityId = "pending", Public = false } };
        var original = DuelStateDigest.Compute(state);
        state.TriggerCostIds.Add(42);
        Assert.NotEqual(original, DuelStateDigest.Compute(state));
        original = DuelStateDigest.Compute(state);
        state.TurnFacts.Add(new DuelEvent { Id = 1, PresentCards = new List<CardLastKnown> {
            new CardLastKnown { Ref = new CardRef(77, 3), DefinitionId = "private-before" } } });
        Assert.NotEqual(original, DuelStateDigest.Compute(state));
        original = DuelStateDigest.Compute(state);
        state.TurnFacts[0].PresentCards[0].DefinitionId = "changed-private-fact";
        Assert.NotEqual(original, DuelStateDigest.Compute(state));
    }

    [Fact]
    public void Replay_detects_a_different_visible_event_even_when_the_recorded_state_is_identical()
    {
        var catalog = DuelCardCatalog.CreateDefault();
        var start = DuelRulePackage.CreateDefault(catalog).Freeze(new DuelStartRecord { MainDecks = new[] {
            Enumerable.Repeat("89631139", 12).ToArray(), Enumerable.Repeat("89631139", 12).ToArray() }, Seed = 17 });
        var engine = new DuelEngine(catalog, start);
        var recorder = new DuelReplayRecorder(start, engine.State);
        var command = new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 };
        var result = engine.Apply(command);
        result.Events.Add(new DuelEvent { Id = 123, Kind = DuelEventKind.Revealed, VisibleToMask = 3, DefinitionId = "forged" });
        recorder.Append(command, result, DuelStateDigest.Compute(engine.State));
        var outcome = DuelReplayRunner.Run(catalog, recorder.Capture());
        Assert.False(outcome.Verified);
        Assert.Equal(1, outcome.MismatchSequence);
    }

    [Fact]
    public void Replay_rejects_an_initial_state_that_does_not_match_its_frozen_start()
    {
        var catalog = DuelCardCatalog.CreateDefault();
        var start = DuelRulePackage.CreateDefault(catalog).Freeze(new DuelStartRecord { MainDecks = new[] {
            Enumerable.Repeat("89631139", 12).ToArray(), Enumerable.Repeat("89631139", 12).ToArray() }, Seed = 17 });
        var engine = new DuelEngine(catalog, start);
        start.Seed = 19;
        var recorder = new DuelReplayRecorder(start, engine.State);
        Assert.Equal("INITIAL_STATE_MISMATCH", DuelReplayRunner.Run(catalog, recorder.Capture()).Error);
    }

    [Fact]
    public void A_rule_package_freezes_all_versions_and_rejects_a_changed_protocol_before_replay()
    {
        var catalog = DuelCardCatalog.CreateDefault();
        var package = DuelRulePackage.CreateDefault(catalog);
        var start = package.Freeze(new DuelStartRecord { MainDecks = new[] { Enumerable.Repeat("89631139", 12).ToArray(),
            Enumerable.Repeat("89631139", 12).ToArray() }, Seed = 734 });
        Assert.True(package.Matches(start));
        Assert.NotEmpty(start.NameCatalogHash); Assert.NotEmpty(start.BanlistHash); Assert.NotEmpty(start.RulePackageHash);
        start.ProtocolVersion++;
        var outcome = DuelReplayRunner.Run(catalog, new DuelReplayRecorder(start).Capture());
        Assert.False(outcome.Verified);
        Assert.Equal("RULE_PACKAGE_MISMATCH", outcome.Error);
    }

    [Fact]
    public void Digest_includes_pending_trigger_continuation_and_rule_package_identity()
    {
        var state = new DuelState();
        var before = DuelStateDigest.Compute(state);
        state.ContinuationAfterTriggers = "resume-battle";
        Assert.NotEqual(before, DuelStateDigest.Compute(state));
        before = DuelStateDigest.Compute(state);
        state.PendingTriggers.Add(new PendingTrigger { AbilityId = "14558127.1", Source = new CardRef(1, 2), EventId = 12 });
        Assert.NotEqual(before, DuelStateDigest.Compute(state));
        before = DuelStateDigest.Compute(state);
        state.RulePackageHash = "frozen-package";
        Assert.NotEqual(before, DuelStateDigest.Compute(state));
    }

    [Fact]
    public void Replay_verifies_every_accepted_rejected_and_system_command_at_its_recorded_revision()
    {
        var catalog = DuelCardCatalog.CreateDefault();
        var start = DuelRulePackage.CreateDefault(catalog).Freeze(new DuelStartRecord { MainDecks = new[] { Enumerable.Repeat("89631139", 12).ToArray(),
            Enumerable.Repeat("89631139", 12).ToArray() }, Seed = 734 });
        var engine = new DuelEngine(catalog, start);
        var recorder = new DuelReplayRecorder(start, engine.State);
        foreach (var command in new[] { new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 },
            new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 },
            new DuelCommand { Kind = DuelCommandKind.Timeout, Player = engine.State.WaitingSeat } })
            recorder.Append(command, engine.Apply(command), DuelStateDigest.Compute(engine.State));

        var replayed = DuelReplayRunner.Run(catalog, recorder.Capture());

        Assert.True(replayed.Verified);
        Assert.Equal(3, replayed.CheckedCommands);
        Assert.Equal(DuelStateDigest.Compute(engine.State), replayed.FinalStateHash);
    }

    [Fact]
    public void State_digest_preserves_deck_order_but_ignores_dictionary_insertion_order()
    {
        var first = new DuelState();
        var second = new DuelState();
        first.Players[0].Deck.AddRange(new[] { 1, 2, 3 });
        second.Players[0].Deck.AddRange(new[] { 1, 2, 3 });
        first.UsedAbilities.Add("b", 2); first.UsedAbilities.Add("a", 1);
        second.UsedAbilities.Add("a", 1); second.UsedAbilities.Add("b", 2);

        Assert.Equal(DuelStateDigest.Compute(first), DuelStateDigest.Compute(second));
        second.Players[0].Deck.Reverse();
        Assert.NotEqual(DuelStateDigest.Compute(first), DuelStateDigest.Compute(second));
    }

    [Fact]
    public void ReplayFreezesTheStartAndCommandInsteadOfKeepingCallersMutableArrays()
    {
        var start = new DuelStartRecord { Seed = 74, MainDecks = new[] { new[] { "original" }, new[] { "opponent" } } };
        var recorder = new DuelReplayRecorder(start);
        var command = new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0, DecisionId = 8,
            Options = new[] { "chosen-option" }, Cards = new[] { 42 } };
        recorder.Append(command, new DuelStepResult { Accepted = true, Revision = 1 }, "hash-1");
        start.MainDecks[0][0] = "tampered";
        command.Options[0] = "tampered";
        command.Cards[0] = 99;
        var replay = recorder.Capture();

        Assert.Equal("original", replay.Start.MainDecks[0][0]);
        var entry = Assert.Single(replay.Entries);
        Assert.Equal(new[] { "chosen-option" }, entry.Command.Options);
        Assert.Equal(new[] { 42 }, entry.Command.Cards);
        Assert.Equal("hash-1", entry.StateHash);
        replay.Start.MainDecks[0][0] = "second-tamper";
        entry.Command.Options[0] = "second-tamper";
        Assert.Equal("original", recorder.Capture().Start.MainDecks[0][0]);
        Assert.Equal("chosen-option", recorder.Capture().Entries[0].Command.Options[0]);
    }
}
