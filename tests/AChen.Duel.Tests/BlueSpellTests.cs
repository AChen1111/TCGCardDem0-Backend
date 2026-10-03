using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class BlueSpellTests
{
    [Fact]
    public void Return_of_dragon_lords_offers_optional_graveyard_banish_to_replace_dragon_destruction()
    {
        var destroyer = new DestructionFixture();
        var fixture = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "70368879", "06853254", "89631139" }.Concat(Enumerable.Repeat("89631139", 37)).ToArray(),
                Enumerable.Repeat("89631139", 40).ToArray() } }, new DuelAbilityRegistry(new[] { destroyer }));
        // This fixture starts with a face-up dragon and Return of the Dragon Lords already in the graveyard.
        var lord = fixture.State.Cards.Single(c => c.DefinitionId == "06853254");
        var target = fixture.State.Cards.First(c => c.DefinitionId == "89631139" && c.Zone == DuelZone.Hand && c.Owner == 0);
        lord.Zone = DuelZone.Graveyard; lord.Position = CardPosition.FaceUp;
        target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack; target.Slot = 0;
        while (fixture.State.Phase != DuelPhase.Main1) PassTwice(fixture);
        Activate(fixture, fixture.State.Cards.Single(c => c.DefinitionId == "70368879"), "fixture.destroy");
        PassTwice(fixture);
        Assert.Equal(DecisionKind.ChooseMode, fixture.State.PendingDecision?.Kind);
        var protection = fixture.State.PendingDecision!.Options.Single(o => o.HasCard && o.Card.InstanceId == lord.InstanceId);
        Answer(fixture, protection.Id);
        Assert.Equal(DuelZone.Banished, lord.Zone);
        Assert.Equal(DuelZone.Monster, target.Zone);
    }

    [Fact]
    public void Return_of_dragon_lords_targets_a_graveyard_level_eight_dragon_and_restores_it_in_chosen_defense()
    {
        var duel = Create("06853254");
        var source = duel.State.Cards.Single(c => c.DefinitionId == "06853254");
        var target = duel.State.Cards.First(c => c.Owner == 0 && c.Zone == DuelZone.Hand && c.DefinitionId == "89631139");
        target.Zone = DuelZone.Graveyard; target.Position = CardPosition.FaceUp; // Initial graveyard fixture.
        Activate(duel, source, "06853254.1", target.InstanceId);
        PassTwice(duel);
        Assert.Equal(DecisionKind.ChooseZone, duel.State.PendingDecision?.Kind);
        Answer(duel, "2");
        Assert.Equal(DecisionKind.ChoosePosition, duel.State.PendingDecision?.Kind);
        Answer(duel, "defense");
        Assert.Equal(DuelZone.Monster, target.Zone);
        Assert.Equal(2, target.Slot);
        Assert.Equal(CardPosition.FaceUpDefense, target.Position);
        Assert.Equal(DuelZone.Graveyard, source.Zone);
        Assert.Null(duel.State.PendingDecision);
    }

    [Fact]
    public void Melody_pays_one_discard_before_response_and_searches_one_or_two_matching_dragons()
    {
        var duel = Create("48800175");
        var source = duel.State.Cards.Single(c => c.DefinitionId == "48800175");
        var cost = duel.State.Cards.First(c => c.Zone == DuelZone.Hand && c.DefinitionId == "89631139");
        Activate(duel, source, "48800175.1", costs: new[] { cost.InstanceId });
        Assert.Equal(DuelZone.Graveyard, cost.Zone);
        Assert.Equal(3, duel.State.Cards.Count(c => c.Owner == 0 && c.Zone == DuelZone.Hand));
        PassTwice(duel);
        Assert.Equal(1, duel.State.PendingDecision!.Min);
        Assert.Equal(2, duel.State.PendingDecision.Max);
        Answer(duel, duel.State.PendingDecision.Options.Take(2).Select(o => o.Id).ToArray());
        Assert.Equal(5, duel.State.Cards.Count(c => c.Owner == 0 && c.Zone == DuelZone.Hand));
        Assert.Equal(DuelZone.Graveyard, source.Zone);
        Assert.Null(duel.State.PendingDecision);
    }

    [Fact]
    public void Dragon_shrine_sends_a_normal_dragon_then_optionally_sends_a_second_dragon()
    {
        var duel = Create("41620959", "41620959");
        var source = duel.State.Cards.First(c => c.DefinitionId == "41620959");
        Activate(duel, source, "41620959.1");
        PassTwice(duel);
        Assert.Equal(DecisionKind.ChooseCards, duel.State.PendingDecision?.Kind);
        var first = duel.State.PendingDecision!.Options[0].Card.InstanceId;
        Answer(duel, duel.State.PendingDecision.Options[0].Id);
        Assert.Equal(DuelZone.Graveyard, duel.State.Cards.Single(c => c.InstanceId == first).Zone);
        Assert.Equal(0, duel.State.PendingDecision!.Min);
        Assert.Equal(1, duel.State.PendingDecision.Max);
        var second = duel.State.PendingDecision.Options[0].Card.InstanceId;
        Answer(duel, duel.State.PendingDecision.Options[0].Id);
        Assert.NotEqual(first, second);
        Assert.Equal(DuelZone.Graveyard, duel.State.Cards.Single(c => c.InstanceId == second).Zone);
        Assert.Null(duel.State.PendingDecision);
        PassTwice(duel);
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.AbilityId == "41620959.1");
    }

    static void Activate(DuelEngine duel, DuelCardState source, string ability, int target = 0, int[]? costs = null) =>
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = source.InstanceId,
            AbilityId = ability, TargetId = target, Cards = costs ?? Array.Empty<int>() }).Accepted);
    static void PassTwice(DuelEngine duel)
    {
        for (int i = 0; i < 2; i++) Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass,
            Player = duel.State.WaitingSeat }).Accepted);
    }
    static void Answer(DuelEngine duel, params string[] choices) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Answer, Player = duel.State.PendingDecision!.Player,
        DecisionId = duel.State.PendingDecision.Id, Options = choices }).Accepted);
    static DuelEngine Create(params string[] opening)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { opening.Concat(Enumerable.Repeat("89631139", 40 - opening.Length)).ToArray(),
                Enumerable.Repeat("89631139", 40).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) PassTwice(duel);
        return duel;
    }

    sealed class DestructionFixture : IAbilityHandler
    {
        public string CardId => "70368879";
        public string AbilityId => "fixture.destroy";
        public int Speed => 1;
        public bool CanActivate(EffectContext context) => true;
        public string ValidateActivation(EffectContext context, DuelCommand command) => "";
        public void PayCost(EffectContext context, DuelCommand command, DuelChainLink link) { }
        public void Resolve(EffectContext context, DuelChainLink link) => context.DestroyMany(link,
            context.State.Cards.Where(c => c.Controller == 0 && c.Zone == DuelZone.Monster).ToArray());
    }
}
