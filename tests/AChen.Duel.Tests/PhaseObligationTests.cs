using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class PhaseObligationTests
{
    [Fact]
    public void A_temporarily_banished_monster_returns_by_rule_with_its_original_position_without_a_new_summon()
    {
        var duel = Create(context =>
        {
            var target = context.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand);
            Assert.True(context.SpecialSummon(target, 0, 2, CardPosition.FaceUpDefense));
            context.Move(target, DuelZone.Banished);
            context.AddEffect(new DuelEffectRecord { Kind = EffectRecordKind.DelayedReturn, Target = target.Ref, Player = 0,
                ExpiresTurn = 2, ReturnPosition = CardPosition.FaceUpDefense, ReturnSlot = 2 });
        });
        ResolveFixture(duel);
        var target = duel.State.Cards.Single(c => c.Owner == 0 && c.Zone == DuelZone.Banished);
        EnterEnd(duel); Pass(duel); Pass(duel);
        while (!(duel.State.Turn == 2 && duel.State.Phase == DuelPhase.End)) Pass(duel);
        Assert.Equal(DecisionKind.ChooseZone, duel.State.PendingDecision?.Kind);
        var decision = duel.State.PendingDecision!;
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0, DecisionId = decision.Id, Options = new[] { "1" } });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(DuelZone.Monster, target.Zone);
        Assert.Equal(1, target.Slot);
        Assert.Equal(CardPosition.FaceUpDefense, target.Position);
        Assert.DoesNotContain(result.Events, e => e.Kind == DuelEventKind.Summoned);
        Assert.Contains(result.Events, e => e.Kind == DuelEventKind.Moved && e.Cause == MoveCause.Rule);
    }

    [Fact]
    public void End_shuffle_returns_random_excess_to_deck_before_the_six_card_discard_obligation()
    {
        var duel = Create(context => context.AddEffect(new DuelEffectRecord { Kind = EffectRecordKind.EndShuffleHand,
            Player = 0, Value = 6, ExpiresTurn = context.State.Turn }), openingHand: 9);
        ResolveFixture(duel);
        Assert.Equal(8, duel.State.Cards.Count(c => c.Controller == 0 && c.Zone == DuelZone.Hand));
        var deckBefore = duel.State.Players[0].Deck.Count;
        var randomBefore = duel.State.RandomState;
        EnterEnd(duel);
        Pass(duel); Pass(duel);
        Assert.Equal(6, duel.State.Cards.Count(c => c.Controller == 0 && c.Zone == DuelZone.Hand));
        Assert.Equal(deckBefore + 2, duel.State.Players[0].Deck.Count);
        Assert.NotEqual(randomBefore, duel.State.RandomState);
        Assert.Null(duel.State.PendingDecision);
        Assert.DoesNotContain(duel.State.Effects, e => e.Kind == EffectRecordKind.EndShuffleHand);
    }

    [Fact]
    public void Source_zone_limited_lingering_draw_ignores_opponent_summons_from_hand()
    {
        var duel = Create(context =>
        {
            context.AddEffect(new DuelEffectRecord { Kind = EffectRecordKind.DrawOnOpponentSpecialSummon,
                Player = 0, Value = 1 << (int)DuelZone.Deck, ExpiresTurn = context.State.Turn });
            var monster = context.State.Cards.First(c => c.Owner == 1 && c.Zone == DuelZone.Hand);
            Assert.True(context.SpecialSummon(monster, 1, 0, CardPosition.FaceUpAttack));
        }, actor: 1);
        ResolveFixture(duel);
        Assert.Equal(5, duel.State.Cards.Count(c => c.Controller == 0 && c.Zone == DuelZone.Hand));
        Assert.False(duel.State.Finished);
    }

    [Fact]
    public void Delayed_revival_resumes_card_zone_and_position_choices_at_next_standby()
    {
        var duel = Create(context =>
        {
            var monster = context.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand);
            context.Move(monster, DuelZone.Graveyard);
            context.AddEffect(new DuelEffectRecord { Kind = EffectRecordKind.DelayedSummon, Player = 0,
                Target = monster.Ref, ExpiresTurn = 2, ExpiresPhase = DuelPhase.Standby });
        });
        ResolveFixture(duel);
        var monster = duel.State.Cards.Single(c => c.Zone == DuelZone.Graveyard && c.DefinitionId == "89631139");
        EnterEnd(duel);
        Pass(duel); Pass(duel); Pass(duel); Pass(duel);
        Assert.Equal(DuelPhase.Standby, duel.State.Phase);
        Assert.Equal(DecisionKind.ChooseCards, duel.State.PendingDecision?.Kind);
        Answer(duel, duel.State.PendingDecision!.Options.Single().Id);
        Assert.Equal(DecisionKind.ChooseZone, duel.State.PendingDecision?.Kind);
        Answer(duel, "2");
        Assert.Equal(DecisionKind.ChoosePosition, duel.State.PendingDecision?.Kind);
        Answer(duel, ((int)CardPosition.FaceUpDefense).ToString());
        Assert.Equal(DuelZone.Monster, monster.Zone);
        Assert.Equal(0, monster.Controller);
        Assert.Equal(2, monster.Slot);
        Assert.Equal(CardPosition.FaceUpDefense, monster.Position);
        Assert.Null(duel.State.PendingDecision);
        Assert.DoesNotContain(duel.State.Effects, e => e.Kind == EffectRecordKind.DelayedSummon);
    }

    [Fact]
    public void A_phase_skip_applies_to_the_specified_players_next_battle_phase_once()
    {
        var duel = Create(context => context.AddEffect(new DuelEffectRecord { Kind = EffectRecordKind.SkipPhase,
            Player = 1, Value = (int)DuelPhase.Battle, ExpiresTurn = 2, ExpiresPhase = DuelPhase.Battle }));
        ResolveFixture(duel);
        EnterEnd(duel);
        Pass(duel); Pass(duel);
        while (duel.State.Phase != DuelPhase.Main1 || duel.State.Window != TimingWindow.Open) Pass(duel);
        Assert.Equal(1, duel.State.TurnPlayer);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 1, Phase = DuelPhase.Battle }).Accepted);
        Pass(duel);
        Assert.Equal(DuelPhase.Main2, duel.State.Phase);
        Assert.DoesNotContain(duel.State.Effects, e => e.Kind == EffectRecordKind.SkipPhase);
    }

    [Fact]
    public void Temporary_control_returns_at_end_completion_with_the_same_field_existence()
    {
        var duel = Create(context =>
        {
            var monster = context.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand);
            Assert.True(context.SpecialSummon(monster, 1, 0, CardPosition.FaceUpAttack));
            context.AddEffect(new DuelEffectRecord { Kind = EffectRecordKind.ReturnControl, Player = 0,
                Target = monster.Ref, ExpiresTurn = context.State.Turn, ExpiresPhase = DuelPhase.End });
        });
        ResolveFixture(duel);
        var target = duel.State.Cards.Single(c => c.Zone == DuelZone.Monster);
        var reference = target.Ref;
        EnterEnd(duel);
        Assert.Equal(1, target.Controller);
        Pass(duel); Pass(duel);
        Assert.Equal(0, target.Controller);
        Assert.Equal(reference, target.Ref);
        Assert.DoesNotContain(duel.State.Effects, e => e.Kind == EffectRecordKind.ReturnControl);
    }

    [Fact]
    public void Scheduled_destruction_occurs_when_its_end_phase_arrives_and_is_consumed()
    {
        var duel = Create(context =>
        {
            var monster = context.State.Cards.First(c => c.Controller == 0 && c.Zone == DuelZone.Hand);
            Assert.True(context.SpecialSummon(monster, 0, 0, CardPosition.FaceUpAttack));
            context.AddEffect(new DuelEffectRecord { Kind = EffectRecordKind.DelayedDestroy, Player = 0,
                Target = monster.Ref, ExpiresTurn = context.State.Turn, ExpiresPhase = DuelPhase.End });
        });
        ResolveFixture(duel);
        var target = duel.State.Cards.Single(c => c.Zone == DuelZone.Monster);
        EnterEnd(duel);
        Assert.Equal(DuelZone.Graveyard, target.Zone);
        Assert.DoesNotContain(duel.State.Effects, e => e.Kind == EffectRecordKind.DelayedDestroy);
    }

    [Fact]
    public void A_lingering_draw_draws_once_for_one_simultaneous_opponent_summon_group()
    {
        var duel = Create(context =>
        {
            context.AddEffect(new DuelEffectRecord { Kind = EffectRecordKind.DrawOnOpponentSpecialSummon,
                Player = 0, ExpiresTurn = context.State.Turn });
            foreach (var card in context.State.Cards.Where(c => c.Controller == 1 && c.Zone == DuelZone.Hand).Take(2).ToArray())
                Assert.True(context.SpecialSummon(card, 1, card.InstanceId % 2, CardPosition.FaceUpAttack));
        }, actor: 1);
        ResolveFixture(duel);
        Assert.Equal(2, duel.State.Cards.Count(c => c.Controller == 1 && c.Zone == DuelZone.Monster));
        Assert.Equal(6, duel.State.Cards.Count(c => c.Controller == 0 && c.Zone == DuelZone.Hand));
    }

    static void ResolveFixture(DuelEngine duel)
    {
        var spell = duel.State.Cards.First(c => c.Owner == duel.State.TurnPlayer && c.DefinitionId == "70368879");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = duel.State.TurnPlayer, CardId = spell.InstanceId,
            AbilityId = "fixture.phase" }).Accepted);
        Pass(duel); Pass(duel);
    }

    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass,
        Player = duel.State.WaitingSeat }).Accepted);

    static void Answer(DuelEngine duel, string option) => Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer,
        Player = duel.State.PendingDecision!.Player, DecisionId = duel.State.PendingDecision.Id, Options = new[] { option } }).Accepted);

    static void EnterEnd(DuelEngine duel)
    {
        while (duel.State.Window != TimingWindow.Open) Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = duel.State.TurnPlayer,
            Phase = DuelPhase.End }).Accepted);
        Pass(duel);
        Assert.Equal(DuelPhase.End, duel.State.Phase);
    }

    static DuelEngine Create(Action<EffectContext> execute, int openingHand = 5, int actor = 0)
    {
        var sourceDeck = new[] { "70368879" }.Concat(Enumerable.Repeat("89631139", 39)).ToArray();
        var otherDeck = Enumerable.Repeat("89631139", 40).ToArray();
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false, OpeningHand = openingHand, FirstPlayer = actor,
            MainDecks = actor == 0 ? new[] { sourceDeck, otherDeck } : new[] { otherDeck, sourceDeck } }, new DuelAbilityRegistry(new[] { new FixtureHandler(execute) }));
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        return duel;
    }

    sealed class FixtureHandler(Action<EffectContext> execute) : IAbilityHandler
    {
        public string AbilityId => "fixture.phase";
        public string CardId => "70368879";
        public int Speed => 1;
        public bool CanActivate(EffectContext context) => true;
        public string ValidateActivation(EffectContext context, DuelCommand command) => "";
        public void PayCost(EffectContext context, DuelCommand command, DuelChainLink link) { }
        public void Resolve(EffectContext context, DuelChainLink link) => execute(context);
    }
}
