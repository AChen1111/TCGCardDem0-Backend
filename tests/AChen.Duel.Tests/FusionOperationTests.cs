using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class FusionOperationTests
{
    [Fact]
    public void FusionEffectConsumesValidMaterialsAndRecordsAProperFusionSummon()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "24094653", "40044918", "89943723" }, Array.Empty<string>() },
            ExtraDecks = new[] { new[] { "22908820" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(new[] { new Fuse() }));
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        foreach (var card in duel.State.Cards.Where(c => c.Zone == DuelZone.Deck)) card.Zone = DuelZone.Hand;
        duel.State.Players[0].Deck.Clear();
        var source = duel.State.Cards.Single(c => c.DefinitionId == "24094653");
        var target = duel.State.Cards.Single(c => c.Zone == DuelZone.ExtraDeck);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = source.InstanceId, AbilityId = "test.fusion" }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 1 }).Accepted);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(DuelZone.Monster, target.Zone);
        Assert.True(target.ProperlySummoned);
        Assert.Equal(SummonMethod.Fusion, target.SummonMethod);
        Assert.Equal(new[] { "40044918", "89943723" }, target.SummonMaterialDefinitions.OrderBy(id => id));
        Assert.All(duel.State.Cards.Where(c => c.DefinitionId == "40044918" || c.DefinitionId == "89943723"),
            c => Assert.Equal(DuelZone.Graveyard, c.Zone));
        Assert.All(result.Events.Where(e => e.Kind == DuelEventKind.Moved && e.WasSummonMaterial), e =>
        { Assert.Equal(MoveCause.Effect, e.Cause); Assert.Equal(SummonMethod.Fusion, e.MaterialMethod); });
    }

    sealed class Fuse : IAbilityHandler
    {
        public string CardId => "24094653";
        public string AbilityId => "test.fusion";
        public int Speed => 1;
        public bool CanActivate(EffectContext context) => true;
        public string ValidateActivation(EffectContext context, DuelCommand command) => "";
        public void PayCost(EffectContext context, DuelCommand command, DuelChainLink link) { }
        public void Resolve(EffectContext context, DuelChainLink link)
        {
            var target = context.State.Cards.Single(c => c.Zone == DuelZone.ExtraDeck);
            var groups = context.Engine.GetFusionMaterialGroups(target, context.Player,
                context.State.Cards.Where(c => c.Zone == DuelZone.Hand));
            context.Engine.FusionSummonByEffect(target, groups.Single(), context.Player, 0,
                CardPosition.FaceUpAttack, link.Source);
        }
    }
}
