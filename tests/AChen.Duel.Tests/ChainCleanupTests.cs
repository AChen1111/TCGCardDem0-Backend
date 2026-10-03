using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class ChainCleanupTests
{
    [Fact]
    public void ResolvedQuickPlayStaysOnFieldUntilTheEntireChainHasFinished()
    {
        // 测试程序只验证连锁清理时机，不替代两张卡的真实卡效实现。
        var record = new DuelStartRecord { Shuffle = false, MainDecks = new[] {
            new[] { "70368879", "21143940" }.Concat(Enumerable.Repeat("89631139", 38)).ToArray(),
            Enumerable.Repeat("89631139", 40).ToArray() } };
        var registry = new DuelAbilityRegistry(new IAbilityHandler[] { new Program("70368879", 1, true), new Program("21143940", 2, false) });
        var engine = new DuelEngine(DuelCardCatalog.CreateDefault(), record, registry);
        while (engine.State.Phase != DuelPhase.Main1) Pass(engine);
        Assert.True(engine.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = 1, AbilityId = "70368879.test" }).Accepted);
        Pass(engine);
        Assert.True(engine.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = 2, AbilityId = "21143940.test", Slot = 1 }).Accepted);
        Pass(engine); Pass(engine);
        var decision = Assert.IsType<DuelDecision>(engine.State.PendingDecision);
        Assert.Equal(DuelZone.SpellTrap, engine.State.Cards.Single(c => c.InstanceId == 2).Zone);
        Assert.True(engine.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0,
            DecisionId = decision.Id, Options = new[] { "yes" } }).Accepted);
        Assert.All(engine.State.Cards.Where(c => c.InstanceId is 1 or 2), c => Assert.Equal(DuelZone.Graveyard, c.Zone));
    }

    static void Pass(DuelEngine engine) => Assert.True(engine.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = engine.State.WaitingSeat }).Accepted);

    sealed class Program(string id, int speed, bool choose) : IAbilityHandler
    {
        public string CardId => id;
        public string AbilityId => id + ".test";
        public int Speed => speed;
        public bool CanActivate(EffectContext context) => true;
        public string ValidateActivation(EffectContext context, DuelCommand command) => "";
        public void PayCost(EffectContext context, DuelCommand command, DuelChainLink link) { }
        public void Resolve(EffectContext context, DuelChainLink link)
        {
            if (!choose || link.Step != 0) return;
            link.Step = 1;
            context.State.PendingDecision = new DuelDecision { Id = context.State.NextDecisionId++, Player = 0, Min = 1, Max = 1,
                Kind = DecisionKind.YesNo, Continuation = AbilityId,
                Options = new() { new DecisionOption { Id = "yes", Value = "yes" } } };
            context.State.Window = TimingWindow.Decision; context.State.WaitingSeat = 0;
        }
    }
}
