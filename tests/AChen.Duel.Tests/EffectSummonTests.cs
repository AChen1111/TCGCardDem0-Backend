using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class EffectSummonTests
{
    [Fact]
    public void SummoningToOpponentFieldRecordsTheEffectCasterAsSummoner()
    {
        var duel = Create("89631139", false, false, 1);
        var source = duel.State.Cards.Single(c => c.DefinitionId == "26077389");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = source.InstanceId, AbilityId = "test.revive" }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 1 }).Accepted);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 });
        Assert.True(result.Accepted, result.Error);
        var summoned = Assert.Single(result.Events.Where(e => e.Kind == DuelEventKind.Summoned));
        Assert.Equal(0, summoned.Player);
        Assert.Equal(0, summoned.EffectPlayer);
        Assert.Equal(1, summoned.After.Controller);
        Assert.Equal(DuelZone.Graveyard, summoned.From);
        Assert.Equal(0, summoned.After.Owner);
    }

    [Fact]
    public void EffectCannotReviveAnExtraDeckMonsterThatWasNotProperlySummonedEvenIgnoringConditions()
    {
        var duel = Create("40908371", false, true);
        var source = duel.State.Cards.Single(c => c.DefinitionId == "26077389");
        var target = duel.State.Cards.Single(c => c.DefinitionId == "40908371");
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.AbilityId == "test.revive");
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = source.InstanceId, AbilityId = "test.revive" });
        Assert.False(result.Accepted);
        Assert.Equal(DuelZone.Graveyard, target.Zone);
    }

    static DuelEngine Create(string targetId, bool proper, bool ignore = false, int destination = 0)
    {
        var ability = new ReviveAbility(targetId, ignore, destination);
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389", targetId }, Array.Empty<string>() },
            OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(new[] { ability }));
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        foreach (var card in duel.State.Cards)
        {
            duel.State.Players[0].Deck.Remove(card.InstanceId);
            card.Zone = card.DefinitionId == "26077389" ? DuelZone.Hand : DuelZone.Graveyard;
            card.ProperlySummoned = proper;
        }
        return duel;
    }

    sealed class ReviveAbility : IAbilityHandler
    {
        readonly string targetId;
        readonly bool ignore;
        readonly int destination;
        public ReviveAbility(string targetId, bool ignore, int destination)
        { this.targetId = targetId; this.ignore = ignore; this.destination = destination; }
        public string CardId => "26077389";
        public string AbilityId => "test.revive";
        public int Speed => 1;
        DuelCardState Target(EffectContext context) => context.State.Cards.Single(c => c.DefinitionId == targetId);
        public bool CanActivate(EffectContext context) => context.CanSpecialSummon(Target(context), destination, ignore);
        public string ValidateActivation(EffectContext context, DuelCommand command) => "";
        public void PayCost(EffectContext context, DuelCommand command, DuelChainLink link) { }
        public void Resolve(EffectContext context, DuelChainLink link) =>
            context.SpecialSummon(Target(context), destination, 2, CardPosition.FaceUpDefense, ignore);
    }
}
