using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class ContinuousImmunityTests
{
    [Fact]
    public void FelgrandsOwnImmunityStopsSunrisesContinuousAttackAura()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false, OpeningHand = 0,
            MainDecks = new[] { new[] { "22908820", "01639384", "26077389" }, new[] { "89631139" } } });
        // 夹具建立两只光属性怪兽与一个超量素材，之后使用正式命令。
        duel.State.Players[0].Deck.Clear();
        var sunrise = duel.State.Cards[0]; sunrise.Zone = DuelZone.Monster; sunrise.Position = CardPosition.FaceUpAttack;
        var felgrand = duel.State.Cards[1]; felgrand.Zone = DuelZone.Monster; felgrand.Slot = 1; felgrand.Position = CardPosition.FaceUpAttack;
        var material = duel.State.Cards[2]; material.Zone = DuelZone.Material; material.HostInstanceId = felgrand.InstanceId;
        felgrand.Materials.Add(material.InstanceId);
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.FastResponse; duel.State.WaitingSeat = 0;
        Pass(duel); Pass(duel);
        Assert.Equal(3000, felgrand.CurrentAtk);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = felgrand.InstanceId, AbilityId = "01639384.1", TargetId = felgrand.InstanceId, Cards = new[] { material.InstanceId } }).Accepted);
        Pass(duel); Pass(duel);
        Assert.Equal(2800, felgrand.CurrentAtk);
    }
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
}
