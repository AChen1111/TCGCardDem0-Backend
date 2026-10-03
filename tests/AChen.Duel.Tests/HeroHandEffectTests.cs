using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HeroHandEffectTests
{
    [Fact]
    public void AquaDolphinConfirmsTheHandThenDestroysAWeakerChosenMonsterAndDealsDamage()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "17955766", "89943723", "89943723", "89943723", "89943723" }
                .Concat(Enumerable.Repeat("89631139", 35)).ToArray(), new[] { "40044918" }.Concat(Enumerable.Repeat("89631139", 39)).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var dolphin = duel.State.Cards.Single(card => card.DefinitionId == "17955766");
        dolphin.Zone = DuelZone.Monster; dolphin.Position = CardPosition.FaceUpAttack;
        var neos = duel.State.Cards.First(card => card.Owner == 0 && card.DefinitionId == "89943723");
        neos.Zone = DuelZone.Monster; neos.Position = CardPosition.FaceUpAttack; neos.Slot = 1;
        var cost = duel.State.Cards.First(card => card.Owner == 0 && card.Zone == DuelZone.Hand);
        var chosen = duel.State.Cards.Single(card => card.Owner == 1 && card.DefinitionId == "40044918");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = dolphin.InstanceId, AbilityId = "17955766.1", Cards = new[] { cost.InstanceId } }).Accepted);
        Pass(duel); Pass(duel);
        Assert.All(duel.State.Cards.Where(card => card.Owner == 1 && card.Zone == DuelZone.Hand), card => Assert.True((card.RevealedToMask & 1) != 0));
        var choice = duel.State.PendingDecision;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = choice.Player,
            DecisionId = choice.Id, Options = new[] { chosen.InstanceId.ToString() } }).Accepted);
        Assert.Equal(DuelZone.Graveyard, chosen.Zone);
        Assert.Equal(7500, duel.State.Players[1].LifePoints);
        Assert.All(duel.State.Cards.Where(card => card.Owner == 1 && card.Zone == DuelZone.Hand), card => Assert.Equal(0, card.RevealedToMask & 1));
    }

    [Fact]
    public void AquaNeosDestroysOneRandomOpponentHandCardAfterDiscardingItsCost()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "55171412" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var source = duel.State.Cards.Single(card => card.DefinitionId == "55171412");
        source.Zone = DuelZone.Monster; source.Position = CardPosition.FaceUpAttack;
        var cost = duel.State.Cards.First(card => card.Owner == 0 && card.Zone == DuelZone.Hand);
        ulong random = duel.State.RandomState;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = source.InstanceId, AbilityId = "55171412.1", Cards = new[] { cost.InstanceId } }).Accepted);
        Pass(duel); Pass(duel);
        Assert.Equal(DuelZone.Graveyard, cost.Zone);
        Assert.Equal(4, duel.State.Cards.Count(card => card.Owner == 1 && card.Zone == DuelZone.Hand));
        Assert.Single(duel.State.Cards.Where(card => card.Owner == 1 && card.Zone == DuelZone.Graveyard));
        Assert.NotEqual(random, duel.State.RandomState);
    }
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
}
