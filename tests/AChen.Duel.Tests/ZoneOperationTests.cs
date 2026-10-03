using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class ZoneOperationTests
{
    [Fact]
    public void AProtectedExtraDeckMonsterCannotBeReturnedByAnEffect()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389" }, Array.Empty<string>() },
            ExtraDecks = new[] { new[] { "40908371" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(new[] { new ReturnMonster() }));
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        var target = duel.State.Cards.Single(c => c.DefinitionId == "40908371");
        target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack; target.ProperlySummoned = true;
        duel.State.Players[0].ExtraDeck.Clear();
        var source = duel.State.Cards.Single(c => c.DefinitionId == "26077389"); source.Zone = DuelZone.Hand;
        duel.State.Players[0].Deck.Clear();
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.CannotReturnToExtra, Target = target.Ref });
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = source.InstanceId, AbilityId = "test.return" }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 1 }).Accepted);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(DuelZone.Monster, target.Zone);
        Assert.True(target.ProperlySummoned);
        Assert.DoesNotContain(result.Events, e => e.Kind == DuelEventKind.Moved && e.Card.InstanceId == target.InstanceId);
    }

    [Fact]
    public void ReturningAnExtraDeckMonsterToHandReturnsItToExtraAndClearsItsSummonMemory()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389" }, Array.Empty<string>() },
            ExtraDecks = new[] { new[] { "40908371" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(new[] { new ReturnMonster() }));
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        var target = duel.State.Cards.Single(c => c.DefinitionId == "40908371");
        target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack; target.ProperlySummoned = true;
        duel.State.Players[0].ExtraDeck.Clear();
        var source = duel.State.Cards.Single(c => c.DefinitionId == "26077389"); source.Zone = DuelZone.Hand;
        duel.State.Players[0].Deck.Clear();
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = source.InstanceId, AbilityId = "test.return" }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 1 }).Accepted);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(DuelZone.ExtraDeck, target.Zone);
        Assert.False(target.ProperlySummoned);
        Assert.Contains(target.InstanceId, duel.State.Players[0].ExtraDeck);
        var move = Assert.Single(result.Events.Where(e => e.Kind == DuelEventKind.Moved));
        Assert.Equal(3, move.VisibleToMask);
        Assert.Equal(MoveCause.Effect, move.Cause);
        Assert.Equal(DuelZone.Monster, move.Before.Zone);
        Assert.Equal(DuelZone.ExtraDeck, move.After.Zone);
    }

    sealed class ReturnMonster : IAbilityHandler
    {
        public string CardId => "26077389";
        public string AbilityId => "test.return";
        public int Speed => 1;
        public bool CanActivate(EffectContext context) => true;
        public string ValidateActivation(EffectContext context, DuelCommand command) => "";
        public void PayCost(EffectContext context, DuelCommand command, DuelChainLink link) { }
        public void Resolve(EffectContext context, DuelChainLink link) => context.Move(
            context.State.Cards.Single(c => c.DefinitionId == "40908371"), DuelZone.Hand);
    }
}
