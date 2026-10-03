using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HeroContinuousEffectTests
{
    [Fact]
    public void SunriseBoostDependsOnFriendlyAttributesAndStopsWhileItIsNegated()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "89943723", "40044918", "89943723", "89943723", "89943723" }
                .Concat(Enumerable.Repeat("89631139", 35)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "22908820" }, Array.Empty<string>() } });
        var neos = duel.State.Cards.First(card => card.Owner == 0 && card.DefinitionId == "89943723");
        var stratos = duel.State.Cards.Single(card => card.DefinitionId == "40044918");
        var sunrise = duel.State.Cards.Single(card => card.DefinitionId == "22908820");
        Place(neos, 0); Place(stratos, 1); Place(sunrise, 2);
        Pass(duel);
        Assert.Equal(2900, neos.CurrentAtk);
        Assert.Equal(2200, stratos.CurrentAtk);
        Assert.Equal(2900, sunrise.CurrentAtk);
        duel.State.Effects.Add(new DuelEffectRecord { Id = duel.State.NextEffectId++, Kind = EffectRecordKind.TargetNegate,
            Target = sunrise.Ref, ExpiresTurn = duel.State.Turn });
        Pass(duel);
        Assert.Equal(2500, neos.CurrentAtk);
        Assert.Equal(1800, stratos.CurrentAtk);
        Assert.Equal(2500, sunrise.CurrentAtk);
    }

    [Fact]
    public void PhoenixEnforcerLowersOpponentAttackByItsOwnersGraveyardHeroCount()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "60461804" }, Array.Empty<string>() } });
        var graveHeroes = duel.State.Cards.Where(card => card.Owner == 0 && card.Zone == DuelZone.Hand).Take(2).ToArray();
        foreach (var card in graveHeroes) { card.Zone = DuelZone.Graveyard; card.Position = CardPosition.FaceUp; }
        var phoenix = duel.State.Cards.Single(card => card.DefinitionId == "60461804"); Place(phoenix, 0);
        var opponent = duel.State.Cards.First(card => card.Owner == 1 && card.Zone == DuelZone.Hand); Place(opponent, 0);
        Pass(duel);
        Assert.Equal(2600, opponent.CurrentAtk);
        graveHeroes[0].Zone = DuelZone.Banished;
        Pass(duel);
        Assert.Equal(2800, opponent.CurrentAtk);
    }

    [Fact]
    public void InfernalDevicerOnlyBoostsFiendsInItsLinkedZonesByTheirCurrentLevels()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "19324993" }, Array.Empty<string>() } });
        var source = duel.State.Cards.Single(card => card.DefinitionId == "19324993");
        source.Zone = DuelZone.ExtraMonster; source.Slot = 0; source.Position = CardPosition.FaceUpAttack;
        var target = duel.State.Cards.First(card => card.Owner == 0 && card.Zone == DuelZone.Hand);
        Place(target, 0); target.CurrentRace = 8; target.CurrentLevel = 7;
        var outside = duel.State.Cards.Where(card => card.Owner == 0 && card.Zone == DuelZone.Hand).Skip(1).First();
        Place(outside, 4); outside.CurrentRace = 8; outside.CurrentLevel = 7;
        var records = new List<DuelEffectRecord>();
        foreach (var rule in duel.Rules.Get("19324993").CreateContinuousRules())
            rule.Collect(new EffectContext(duel, 0, source.InstanceId), records);
        Assert.Contains(records, record => record.Kind == EffectRecordKind.AddAttack && record.Target.Equals(target.Ref) && record.Value == 700);
        Assert.Contains(records, record => record.Kind == EffectRecordKind.AddDefense && record.Target.Equals(target.Ref) && record.Value == 700);
        Assert.DoesNotContain(records, record => record.Target.Equals(outside.Ref));
    }

    [Fact]
    public void WakeAttackBonusUsesAllFusionMaterialsAndItsMonsterAttackCountUsesFusionMaterialsOnly()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "32828466" }, Array.Empty<string>() } });
        var wake = duel.State.Cards.Single(card => card.DefinitionId == "32828466"); Place(wake, 0);
        wake.SummonMaterialDefinitions.AddRange(new[] { "22908820", "23204029", "89943723" });
        Pass(duel);
        Assert.Equal(3400, wake.CurrentAtk);
        var records = new List<DuelEffectRecord>();
        foreach (var rule in duel.Rules.Get("32828466").CreateContinuousRules()) rule.Collect(new EffectContext(duel, 0, wake.InstanceId), records);
        Assert.Contains(records, record => record.Kind == EffectRecordKind.ExtraMonsterAttacks && record.Value == 1);
    }
    static void Place(DuelCardState card, int slot)
    { card.Zone = DuelZone.Monster; card.Position = CardPosition.FaceUpAttack; card.Slot = slot; }
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
}
