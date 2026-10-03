using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HeroModeEffectTests
{
    [Fact]
    public void StratosChoosesItsSearchAtActivationAndAshCanRespondToThatChosenMode()
    {
        var duel = Create();
        var stratos = duel.State.Cards.Single(card => card.DefinitionId == "40044918");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 0, CardId = stratos.InstanceId }).Accepted);
        Answer(duel, "yes"); Answer(duel, "search");
        Assert.Contains(duel.QueryLegalActions(1), action => action.AbilityId == "14558127.1");
        var ash = duel.State.Cards.Single(card => card.DefinitionId == "14558127");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = ash.InstanceId, AbilityId = "14558127.1" }).Accepted);
        Pass(duel); Pass(duel);
        Assert.Null(duel.State.PendingDecision);
        Assert.DoesNotContain(duel.State.Cards, card => card.DefinitionId == "09411399" && card.Zone == DuelZone.Hand);
    }
    static DuelEngine Create()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "40044918", "89943723", "89943723", "89943723", "89943723" }
                .Concat(Enumerable.Repeat("09411399", 35)).ToArray(), new[] { "14558127" }.Concat(Enumerable.Repeat("89631139", 39)).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
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
