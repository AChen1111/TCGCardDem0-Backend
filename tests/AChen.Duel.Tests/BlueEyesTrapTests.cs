using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class BlueEyesTrapTests
{
    [Fact]
    public void PhoenixWingWindBlastReturnsTheTargetToTheTopOfItsOwnersDeck()
    {
        var engine = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "63356631", "26077389" }, new[] { "89631139", "89631139" } }, OpeningHand = 0 });
        var trap = engine.State.Cards[0]; var cost = engine.State.Cards[1]; var target = engine.State.Cards[2];
        engine.State.Players[0].Deck.Clear(); engine.State.Players[1].Deck.Remove(target.InstanceId);
        trap.Zone = DuelZone.SpellTrap; trap.Position = CardPosition.FaceDown;
        cost.Zone = DuelZone.Hand; target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack;
        engine.State.Phase = DuelPhase.Main1; engine.State.Window = TimingWindow.Open;
        Assert.True(engine.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = trap.InstanceId, AbilityId = "63356631.1", TargetId = target.InstanceId, Cards = new[] { cost.InstanceId } }).Accepted);
        engine.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 1 });
        engine.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 });
        Assert.Equal(DuelZone.Deck, target.Zone);
        Assert.Equal(target.InstanceId, engine.State.Players[1].Deck[0]);
    }

    [Fact]
    public void KarmaCutPaysItsDiscardAndBanishesTheTargetAndGraveyardCopies()
    {
        var engine = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "71587526", "26077389" }, new[] { "89631139", "89631139" } }, OpeningHand = 0 });
        var trap = engine.State.Cards[0]; var cost = engine.State.Cards[1];
        var target = engine.State.Cards[2]; var grave = engine.State.Cards[3];
        foreach (var player in engine.State.Players) player.Deck.Clear();
        trap.Zone = DuelZone.SpellTrap; trap.Position = CardPosition.FaceDown;
        cost.Zone = DuelZone.Hand; target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack;
        grave.Zone = DuelZone.Graveyard; grave.Position = CardPosition.FaceUp;
        engine.State.Phase = DuelPhase.Main1; engine.State.Window = TimingWindow.Open;
        Assert.True(engine.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = trap.InstanceId, AbilityId = "71587526.1", TargetId = target.InstanceId, Cards = new[] { cost.InstanceId } }).Accepted);
        Assert.Equal(DuelZone.Graveyard, cost.Zone);
        engine.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 1 });
        engine.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 });
        Assert.Equal(DuelZone.Banished, target.Zone);
        Assert.Equal(DuelZone.Banished, grave.Zone);
    }
}
