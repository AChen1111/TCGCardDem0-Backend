using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class DestructionOperationTests
{
    [Fact]
    public void SimultaneouslyDestroyingHostAndEquipmentDoesNotSendTheEquipmentTwice()
    {
        var ability = new ProgramAbility("40044918", 99, 1, new[] { DuelZone.Monster }, c => true,
            (c, link) => c.DestroyMany(link, c.State.Cards.Where(card => card.Owner == 0
                && (card.Zone == DuelZone.Monster || card.Zone == DuelZone.SpellTrap))));
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "89631139", "26077389" }, new[] { "40044918" } }, OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(new[] { ability }));
        foreach (var card in duel.State.Cards)
        { duel.State.Players[card.Owner].Deck.Clear(); card.Zone = DuelZone.Monster; card.Position = CardPosition.FaceUpAttack; }
        var host = duel.State.Cards.Single(c => c.DefinitionId == "89631139");
        var equip = duel.State.Cards.Single(c => c.DefinitionId == "26077389");
        equip.Zone = DuelZone.SpellTrap; equip.EquipTarget = host.Ref;
        duel.State.TurnPlayer = 1; duel.State.WaitingSeat = 1;
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = duel.State.Cards.Single(c => c.Owner == 1).InstanceId, AbilityId = "40044918.99" }).Accepted);
        Pass(duel);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat });
        Assert.True(result.Accepted, result.Error);
        var move = Assert.Single(result.Events.Where(e => e.Kind == DuelEventKind.Moved && e.Card.InstanceId == equip.InstanceId));
        Assert.Equal(MoveCause.Effect, move.Cause);
        Assert.Equal(2, result.Events.Count(e => e.Kind == DuelEventKind.Destroyed));
    }

    [Fact]
    public void OneUseDestructionProtectionIsConsumedAndNextDestructionSucceeds()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "89631139" }, new[] { "40044918" } }, OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(new[] { new DestroyGroup() }));
        foreach (var card in duel.State.Cards)
        { duel.State.Players[card.Owner].Deck.Clear(); card.Zone = DuelZone.Monster; card.Position = CardPosition.FaceUpAttack; }
        duel.State.TurnPlayer = 1; duel.State.WaitingSeat = 1;
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        var target = duel.State.Cards.Single(c => c.Owner == 0);
        var source = duel.State.Cards.Single(c => c.Owner == 1);
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.DestroyReplacement,
            Target = target.Ref, Source = source.Ref, Remaining = 1 });
        var command = new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = source.InstanceId, AbilityId = "test.destroy" };
        Assert.True(duel.Apply(command).Accepted); Pass(duel); Pass(duel);
        Assert.Null(duel.State.PendingDecision);
        Assert.Equal(DuelZone.Monster, target.Zone);
        Assert.DoesNotContain(duel.State.Effects, e => e.Kind == EffectRecordKind.DestroyReplacement);
        Pass(duel); Pass(duel);
        Assert.True(duel.Apply(command).Accepted); Pass(duel); Pass(duel);
        Assert.Equal(DuelZone.Graveyard, target.Zone);
        Assert.Equal(8100, duel.State.Players[1].LifePoints);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OptionalDragonReplacementResumesTheWholeGroupWithItsActualDestructionOutcome(bool replace)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "89631139", "26077389", "06853254" }, new[] { "40044918" } },
            OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(new[] { new DestroyGroup() }));
        foreach (var card in duel.State.Cards)
        {
            duel.State.Players[card.Owner].Deck.Clear();
            card.Zone = card.DefinitionId == "06853254" ? DuelZone.Graveyard : DuelZone.Monster;
            card.Position = CardPosition.FaceUpAttack;
        }
        var lord = duel.State.Cards.Single(c => c.DefinitionId == "06853254");
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.DestroyReplacement,
            Source = lord.Ref, Player = 0, Value = 8192 });
        duel.State.TurnPlayer = 1; duel.State.WaitingSeat = 1;
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        var source = duel.State.Cards.Single(c => c.Owner == 1);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = source.InstanceId, AbilityId = "test.destroy" }).Accepted);
        Pass(duel); Pass(duel);
        var decision = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal(0, decision.Player);
        Assert.Equal("destruction.replace", decision.Continuation);
        Assert.Equal(DuelZone.Monster, duel.State.Cards.Single(c => c.DefinitionId == "89631139").Zone);
        var option = decision.Options.Single(o => replace ? o.HasCard : !o.HasCard);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = decision.Player,
            DecisionId = decision.Id, Options = new[] { option.Id } });
        Assert.True(result.Accepted, result.Error);
        Assert.Null(duel.State.PendingDecision);
        Assert.Null(duel.State.PendingDestruction);
        Assert.Empty(duel.State.Chain);
        Assert.Equal(replace ? DuelZone.Monster : DuelZone.Graveyard,
            duel.State.Cards.Single(c => c.DefinitionId == "89631139").Zone);
        Assert.Equal(DuelZone.Graveyard, duel.State.Cards.Single(c => c.DefinitionId == "26077389").Zone);
        Assert.Equal(replace ? DuelZone.Banished : DuelZone.Graveyard, lord.Zone);
        Assert.Equal(replace ? 8100 : 8200, duel.State.Players[1].LifePoints);
    }

    static void Pass(DuelEngine duel)
    {
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat });
        Assert.True(result.Accepted, result.Error);
    }

    sealed class DestroyGroup : IAbilityHandler
    {
        public string CardId => "40044918";
        public string AbilityId => "test.destroy";
        public int Speed => 1;
        public bool CanActivate(EffectContext context) => true;
        public string ValidateActivation(EffectContext context, DuelCommand command) => "";
        public void PayCost(EffectContext context, DuelCommand command, DuelChainLink link) { }
        public void Resolve(EffectContext context, DuelChainLink link)
        {
            var operation = context.DestroyMany(link, context.State.Cards.Where(c => c.Controller == 0 && c.Zone == DuelZone.Monster));
            if (!operation.Completed) return;
            context.Recover(1, operation.Destroyed.Count * 100);
        }
    }
}
