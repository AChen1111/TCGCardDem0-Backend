using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class BlueEyesXyzTests
{
    [Fact]
    public void FelgrandDetachesOneMaterialAndAppliesBothNegationAndUnaffectedUntilEnd()
    {
        var engine = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "89631139" }, new[] { "26077389" } },
            ExtraDecks = new[] { new[] { "01639384" }, Array.Empty<string>() }, OpeningHand = 0 });
        var material = engine.State.Cards[0]; var knight = engine.State.Cards[1]; var target = engine.State.Cards[2];
        engine.State.Players[0].Deck.Clear(); engine.State.Players[0].ExtraDeck.Clear(); engine.State.Players[1].Deck.Clear();
        knight.Zone = DuelZone.Monster; knight.Position = CardPosition.FaceUpAttack; knight.Materials.Add(material.InstanceId);
        material.Zone = DuelZone.Material; material.HostInstanceId = knight.InstanceId;
        target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack;
        engine.State.Phase = DuelPhase.Main1; engine.State.Window = TimingWindow.Open;
        Assert.True(engine.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = knight.InstanceId, AbilityId = "01639384.1", TargetId = target.InstanceId, Cards = new[] { material.InstanceId } }).Accepted);
        engine.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 1 });
        engine.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 });
        Assert.Equal(DuelZone.Graveyard, material.Zone);
        Assert.True(target.Negated);
        Assert.Contains(engine.State.Effects, e => e.Kind == EffectRecordKind.Unaffected && e.Target.Equals(target.Ref));
    }
}
