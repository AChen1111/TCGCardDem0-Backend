using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class TripleTacticsEffectTests
{
    [Fact]
    public void TalentIsUnavailableBeforeAnOpponentMonsterEffectAndDrawsAfterAshNegatesOnlyTheEffect()
    {
        var duel = Create("25311006");
        var talent = duel.State.Cards.Single(card => card.DefinitionId == "25311006");
        Assert.DoesNotContain(duel.QueryLegalActions(0), action => action.AbilityId == "25311006.1");
        FireAsh(duel);
        int hand = duel.State.Cards.Count(card => card.Owner == 0 && card.Zone == DuelZone.Hand);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = talent.InstanceId, AbilityId = "25311006.1", Options = new[] { "draw" } }).Accepted);
        Pass(duel); Pass(duel);
        Assert.Equal(hand + 1, duel.State.Cards.Count(card => card.Owner == 0 && card.Zone == DuelZone.Hand));
    }

    [Fact]
    public void ThrustSetsANormalSpellThatCannotBeActivatedInTheSameTurn()
    {
        var duel = Create("35269904"); FireAsh(duel);
        var thrust = duel.State.Cards.Single(card => card.DefinitionId == "35269904");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = thrust.InstanceId, AbilityId = "35269904.1" }).Accepted);
        Pass(duel); Pass(duel);
        var choice = duel.State.PendingDecision;
        int selected = choice.Options[0].Card.InstanceId;
        Answer(duel, choice.Options[0].Id); Answer(duel, "set"); Answer(duel, "3");
        var spell = duel.State.Cards.Single(card => card.InstanceId == selected);
        Assert.Equal(DuelZone.SpellTrap, spell.Zone);
        Assert.Equal(CardPosition.FaceDown, spell.Position);
        Pass(duel); Pass(duel);
        Assert.DoesNotContain(duel.QueryLegalActions(0), action => action.Card.InstanceId == selected && action.Kind == DuelCommandKind.Activate);
    }

    [Fact]
    public void TalentHandModeConfirmsAndReturnsOneOpponentCardThenClosesTheTemporaryVisibility()
    {
        var duel = Create("25311006"); FireAsh(duel);
        var talent = duel.State.Cards.Single(card => card.DefinitionId == "25311006");
        int hand = duel.State.Cards.Count(card => card.Owner == 1 && card.Zone == DuelZone.Hand);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = talent.InstanceId, AbilityId = "25311006.1", Options = new[] { "hand" } }).Accepted);
        Pass(duel); Pass(duel);
        Assert.All(duel.State.Cards.Where(card => card.Owner == 1 && card.Zone == DuelZone.Hand), card => Assert.True((card.RevealedToMask & 1) != 0));
        int selected = duel.State.PendingDecision.Options[0].Card.InstanceId;
        Answer(duel, selected.ToString());
        Assert.Equal(DuelZone.Deck, duel.State.Cards.Single(card => card.InstanceId == selected).Zone);
        Assert.Equal(hand - 1, duel.State.Cards.Count(card => card.Owner == 1 && card.Zone == DuelZone.Hand));
        Assert.All(duel.State.Cards.Where(card => card.Owner == 1 && card.Zone == DuelZone.Hand), card => Assert.Equal(0, card.RevealedToMask & 1));
    }

    [Fact]
    public void TalentControlModeMovesTheChosenMonsterAndReturnsItsControllerAtEndPhase()
    {
        var duel = Create("25311006"); FireAsh(duel);
        var monster = duel.State.Cards.First(card => card.Owner == 1 && card.Zone == DuelZone.Hand);
        monster.Zone = DuelZone.Monster; monster.Position = CardPosition.FaceUpAttack;
        var talent = duel.State.Cards.Single(card => card.DefinitionId == "25311006");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = talent.InstanceId, AbilityId = "25311006.1", Options = new[] { "control" } }).Accepted);
        Pass(duel); Pass(duel); Answer(duel, monster.InstanceId.ToString()); Answer(duel, "2");
        Assert.Equal(0, monster.Controller);
        Assert.Equal(2, monster.Slot);
        Pass(duel); Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        for (int step = 0; monster.Controller == 0 && step < 12; step++) Pass(duel);
        Assert.Equal(1, monster.Controller);
    }

    [Fact]
    public void ThrustCanAddInsteadOfSettingOnlyWhenTheOpponentHasAMonsterDuringResolution()
    {
        var duel = Create("35269904"); FireAsh(duel);
        var monster = duel.State.Cards.First(card => card.Owner == 1 && card.Zone == DuelZone.Hand);
        monster.Zone = DuelZone.Monster; monster.Position = CardPosition.FaceUpAttack;
        var thrust = duel.State.Cards.Single(card => card.DefinitionId == "35269904");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = thrust.InstanceId, AbilityId = "35269904.1" }).Accepted);
        Pass(duel); Pass(duel);
        int selected = duel.State.PendingDecision.Options[0].Card.InstanceId;
        Answer(duel, selected.ToString()); Answer(duel, "hand");
        Assert.Equal(DuelZone.Hand, duel.State.Cards.Single(card => card.InstanceId == selected).Zone);
    }
    static DuelEngine Create(string spell)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { spell, "00213326", "89943723", "89943723", "89943723" }
                .Concat(spell == "35269904" ? new[] { "40044918" }.Concat(Enumerable.Repeat("81439173", 34))
                    : Enumerable.Repeat("40044918", 35)).ToArray(), new[] { "14558127" }.Concat(Enumerable.Repeat("89631139", 39)).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        return duel;
    }
    static void FireAsh(DuelEngine duel)
    {
        var call = duel.State.Cards.Single(card => card.DefinitionId == "00213326");
        var ash = duel.State.Cards.Single(card => card.DefinitionId == "14558127");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = call.InstanceId, AbilityId = "00213326.1" }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1, CardId = ash.InstanceId, AbilityId = "14558127.1" }).Accepted);
        Pass(duel); Pass(duel); Pass(duel); Pass(duel);
    }
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
    static void Answer(DuelEngine duel, params string[] options)
    {
        var choice = duel.State.PendingDecision;
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = choice.Player, DecisionId = choice.Id, Options = options });
        Assert.True(result.Accepted, result.Error);
    }
}
