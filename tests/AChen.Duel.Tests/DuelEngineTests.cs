using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class DuelEngineTests
{
    internal static DuelStartRecord Start(string first = "89631139", string second = "89631139", int count = 40) => new()
    {
        MainDecks = new[] { Enumerable.Repeat(first, count).ToArray(), Enumerable.Repeat(second, count).ToArray() },
        FirstPlayer = 0, Shuffle = false, Seed = 91
    };

    [Fact]
    public void StartDealsFiveCardsToBothPlayersWithoutFirstPlayerDrawingAgain()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), Start());
        Assert.Equal(5, duel.State.Cards.Count(x => x.Controller == 0 && x.Zone == DuelZone.Hand));
        Assert.Equal(5, duel.State.Cards.Count(x => x.Controller == 1 && x.Zone == DuelZone.Hand));
        Assert.Equal(35, duel.State.Players[0].Deck.Count);
        Assert.Equal(8000, duel.State.Players[0].LifePoints);
        Assert.Equal(DuelPhase.Draw, duel.State.Phase);
        Assert.Equal(0, duel.State.TurnPlayer);
    }

    [Fact]
    public void BothPlayersMustPassTheCurrentWindowBeforeThePhaseChanges()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), Start());
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 1 }).Accepted);
        Assert.Equal(0, duel.State.Revision);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 }).Accepted);
        Assert.Equal(DuelPhase.Draw, duel.State.Phase);
        Assert.Equal(1, duel.State.WaitingSeat);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 1 }).Accepted);
        Assert.Equal(DuelPhase.Standby, duel.State.Phase);
        Assert.Equal(0, duel.State.WaitingSeat);
    }

    internal static void Pass(DuelEngine engine) => Assert.True(engine.Apply(new DuelCommand
        { Kind = DuelCommandKind.Pass, Player = engine.State.WaitingSeat }).Accepted);
    internal static void ToMain(DuelEngine engine)
    {
        while (engine.State.Phase != DuelPhase.Main1) Pass(engine);
    }

    [Fact]
    public void NormalSummonOpensAResponseWindowAndConsumesTheTurnAllowance()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), Start("26077389"));
        ToMain(duel);
        var hand = duel.State.Cards.Where(x => x.Owner == 0 && x.Zone == DuelZone.Hand).ToArray();
        var summon = new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 0, CardId = hand[0].InstanceId, Slot = 2 };
        Assert.True(duel.Apply(summon).Accepted);
        Assert.Equal(DuelZone.Monster, hand[0].Zone);
        Assert.Equal(2, hand[0].Slot);
        Assert.Equal(TimingWindow.FastResponse, duel.State.Window);
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 0,
            CardId = hand[1].InstanceId, Slot = 3 }).Accepted);
        Pass(duel); Pass(duel);
        Assert.Equal(TimingWindow.Open, duel.State.Window);
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 0,
            CardId = hand[1].InstanceId, Slot = 3 }).Accepted);
        Assert.Equal(DuelZone.Hand, hand[1].Zone);
    }

    [Fact]
    public void FirstTurnCannotEnterBattleAndEndingWaitsForTheOpponent()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), Start());
        ToMain(duel);
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.Battle }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        Assert.Equal(DuelPhase.Main1, duel.State.Phase);
        Assert.Equal(1, duel.State.WaitingSeat);
        Pass(duel);
        Assert.Equal(DuelPhase.End, duel.State.Phase);
        Pass(duel); Pass(duel);
        Assert.Equal(2, duel.State.Turn);
        Assert.Equal(1, duel.State.TurnPlayer);
        Assert.Equal(6, duel.State.Cards.Count(x => x.Owner == 1 && x.Zone == DuelZone.Hand));
    }

    [Fact]
    public void DirectAttackResolvesAfterResponsesAndCannotAttackTwice()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), Start("26077389", "26077389"));
        while (duel.State.Turn == 1) Pass(duel);
        ToMain(duel);
        var attacker = duel.State.Cards.First(x => x.Owner == 1 && x.Zone == DuelZone.Hand);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 1,
            CardId = attacker.InstanceId, Slot = 0 }).Accepted);
        Pass(duel); Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 1, Phase = DuelPhase.Battle }).Accepted);
        Pass(duel);
        var attack = new DuelCommand { Kind = DuelCommandKind.Attack, Player = 1, CardId = attacker.InstanceId };
        Assert.True(duel.Apply(attack).Accepted);
        Assert.Equal(8000, duel.State.Players[0].LifePoints);
        for (int i = 0; i < 20 && duel.State.Window != TimingWindow.Open; i++) Pass(duel);
        Assert.Equal(6500, duel.State.Players[0].LifePoints);
        Assert.False(duel.Apply(attack).Accepted);
    }

    [Fact]
    public void EndPhaseRequiresDiscardToSixBeforePassingTheTurn()
    {
        var start = Start("26077389"); start.OpeningHand = 7;
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), start);
        ToMain(duel);
        duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Phase = DuelPhase.End, Player = 0 });
        Pass(duel); Pass(duel); Pass(duel);
        var choice = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal(1, choice.Min);
        Assert.Equal(1, duel.State.Turn);
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0, DecisionId = choice.Id,
            Options = new[] { choice.Options[0].Id } }).Accepted);
        Assert.Equal(6, duel.State.Cards.Count(c => c.Owner == 0 && c.Zone == DuelZone.Hand));
        Assert.Equal(2, duel.State.Turn);
    }
}
