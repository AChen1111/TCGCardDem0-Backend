using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HeroXyzEffectTests
{
    [Fact]
    public void DugaresChoosesDrawAtActivationDetachesTwoThenDrawsAndDiscards()
    {
        var duel = Create();
        var dugares = duel.State.Cards.Single(card => card.DefinitionId == "66011101");
        var materials = dugares.Materials.ToArray();
        var handBefore = duel.State.Cards.Count(card => card.Owner == 0 && card.Zone == DuelZone.Hand);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = dugares.InstanceId,
            AbilityId = "66011101.1", Options = new[] { "draw" }, Cards = materials }).Accepted);
        Assert.Empty(dugares.Materials);
        Assert.All(materials, id => Assert.Equal(DuelZone.Graveyard, duel.State.Cards.Single(card => card.InstanceId == id).Zone));
        Pass(duel); Pass(duel);
        Assert.Equal(handBefore + 2, duel.State.Cards.Count(card => card.Owner == 0 && card.Zone == DuelZone.Hand));
        Answer(duel, duel.State.PendingDecision.Options[0].Id);
        Assert.Equal(handBefore + 1, duel.State.Cards.Count(card => card.Owner == 0 && card.Zone == DuelZone.Hand));
        Assert.Contains(duel.State.Effects, record => record.Kind == EffectRecordKind.SkipPhase && record.Player == 0 && record.Value == (int)DuelPhase.Draw);
    }

    [Fact]
    public void DugaresDoubleModeDoesNotTargetAndChoosesTheMonsterWhileResolving()
    {
        var duel = Create();
        var dugares = duel.State.Cards.Single(card => card.DefinitionId == "66011101");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = dugares.InstanceId, AbilityId = "66011101.1", Options = new[] { "double" }, Cards = dugares.Materials.ToArray() }).Accepted);
        Assert.Empty(duel.State.Chain.Single().Targets);
        Pass(duel); Pass(duel); Answer(duel, dugares.InstanceId.ToString());
        Assert.Equal(2400, dugares.CurrentAtk);
        Assert.Contains(duel.State.Effects, record => record.Kind == EffectRecordKind.SkipPhase && record.Value == (int)DuelPhase.Battle);
    }

    [Fact]
    public void DugaresRevivalCanBeActivatedUsingAMonsterThatWillBeDetachedAsItsCost()
    {
        var duel = Create();
        var dugares = duel.State.Cards.Single(card => card.DefinitionId == "66011101");
        var materials = dugares.Materials.ToArray();
        Assert.Empty(duel.State.Cards.Where(card => card.Zone == DuelZone.Graveyard));
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = dugares.InstanceId, AbilityId = "66011101.1", Options = new[] { "revive" }, Cards = materials }).Accepted);
        Pass(duel); Pass(duel); Answer(duel, materials[0].ToString()); Answer(duel, "1");
        var revived = duel.State.Cards.Single(card => card.InstanceId == materials[0]);
        Assert.Equal(DuelZone.Monster, revived.Zone);
        Assert.Equal(CardPosition.FaceUpDefense, revived.Position);
    }
    static DuelEngine Create()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { Enumerable.Repeat("26077389", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() },
            ExtraDecks = new[] { new[] { "66011101" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var dugares = duel.State.Cards.Single(card => card.DefinitionId == "66011101");
        dugares.Zone = DuelZone.Monster; dugares.Position = CardPosition.FaceUpAttack;
        foreach (var card in duel.State.Cards.Where(card => card.Owner == 0 && card.Zone == DuelZone.Hand).Take(2))
        { card.Zone = DuelZone.Material; card.HostInstanceId = dugares.InstanceId; dugares.Materials.Add(card.InstanceId); }
        return duel;
    }
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
    static void Answer(DuelEngine duel, params string[] options)
    {
        var choice = duel.State.PendingDecision;
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = choice.Player, DecisionId = choice.Id, Options = options });
        Assert.True(result.Accepted, result.Error);
    }
}
