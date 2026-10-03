using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class EndTurnTimingTests
{
    [Fact]
    public void EndPhaseControlReturnGetsItsTriggerCheckpointBeforeTheTurnChanges()
    {
        var ability = new TriggerProgramAbility("26077389", 99, new[] { DuelZone.Monster }, true, false,
            (c, f) => f.Kind == DuelEventKind.Moved && f.Card.Equals(c.SourceRef)
                && f.Before!.Controller == 1 && f.After!.Controller == 0,
            c => c.State.Phase == DuelPhase.End, (c, l) => c.Recover(c.Player, 100));
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            OpeningHand = 0, MainDecks = new[] { new[] { "26077389", "89631139" }, new[] { "89631139", "89631139" } } },
            new DuelAbilityRegistry(new[] { ability }));
        // 夹具只建立被暂时借走且结束阶段归还的初始局面。
        var source = duel.State.Cards[0]; duel.State.Players[0].Deck.Remove(source.InstanceId);
        source.Zone = DuelZone.Monster; source.Controller = 1; source.Position = CardPosition.FaceUpAttack;
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open; duel.State.WaitingSeat = 0;
        duel.State.Effects.Add(new DuelEffectRecord { Id = duel.State.NextEffectId++, Kind = EffectRecordKind.ReturnControl,
            Player = 0, Target = source.Ref, ExpiresTurn = 1 });
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        Pass(duel); Pass(duel); Pass(duel);
        Assert.Equal(1, duel.State.Turn);
        Assert.Equal(DuelPhase.End, duel.State.Phase);
        Assert.Equal(ability.AbilityId, Assert.Single(duel.State.Chain).AbilityId);
        Pass(duel); Pass(duel);
        Assert.Equal(8100, duel.State.Players[0].LifePoints);
        Assert.Equal(2, duel.State.Turn);
    }

    [Fact]
    public void DrollStillPreventsWhiteStoneSearchTriggeredByEndPhaseHandAdjustment()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord {
            Shuffle = false, OpeningHand = 9, MainDecks = new[] {
                new[] { "00213326", "79814787" }.Concat(Enumerable.Repeat("89631139", 7))
                    .Concat(new[] { "40044918" }).Concat(Enumerable.Repeat("89631139", 30)).ToArray(),
                new[] { "94145021" }.Concat(Enumerable.Repeat("89631139", 39)).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        Activate(duel, 0, 1, "00213326.1");
        Pass(duel); Pass(duel);
        Answer(duel, duel.State.PendingDecision!.Options.Single().Id);
        Pass(duel);
        Activate(duel, 1, 41, "94145021.1");
        Pass(duel); Pass(duel);
        while (duel.State.Window != TimingWindow.Open) Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        Pass(duel); Pass(duel); Pass(duel);
        var discard = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal("end.discard", discard.Continuation);
        var choices = discard.Options.Where(o => o.Card.InstanceId == 2).Concat(
            discard.Options.Where(o => o.Card.InstanceId != 2).Take(discard.Min - 1)).Select(o => o.Id).ToArray();
        int deckCount = duel.State.Players[0].Deck.Count;
        Answer(duel, choices);
        Pass(duel); Pass(duel);
        if (duel.State.PendingDecision != null)
            Answer(duel, duel.State.PendingDecision.Options.First().Id);
        Assert.Equal(6, duel.State.Cards.Count(c => c.Owner == 0 && c.Zone == DuelZone.Hand));
        Assert.Equal(deckCount, duel.State.Players[0].Deck.Count);
    }

    static void Activate(DuelEngine duel, int player, int card, string ability) => Assert.True(duel.Apply(
        new DuelCommand { Kind = DuelCommandKind.Activate, Player = player, CardId = card, AbilityId = ability }).Accepted);
    static void Answer(DuelEngine duel, params string[] options) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Answer, Player = duel.State.PendingDecision!.Player,
        DecisionId = duel.State.PendingDecision.Id, Options = options }).Accepted);
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
}
