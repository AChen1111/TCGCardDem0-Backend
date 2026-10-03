using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class TypedDecisionTests
{
    [Theory]
    [InlineData(DecisionKind.ChooseMode, "draw-two")]
    [InlineData(DecisionKind.DeclareName, "00000483")]
    [InlineData(DecisionKind.ChooseZone, "3")]
    [InlineData(DecisionKind.ChoosePosition, "FaceUpDefense")]
    [InlineData(DecisionKind.YesNo, "yes")]
    public void NonCardAnswerResumesThePendingProgramWithoutLosingItsValue(DecisionKind kind, string value)
    {
        // 测试用程序验证公共决策续接契约，不代表成金哥布林的实际卡效。
        var ability = new DecisionProgram(kind, value);
        var record = new DuelStartRecord { Shuffle = false, MainDecks = new[] {
            new[] { "70368879" }.Concat(Enumerable.Repeat("89631139", 39)).ToArray(),
            Enumerable.Repeat("89631139", 40).ToArray() } };
        var engine = new DuelEngine(DuelCardCatalog.CreateDefault(), record, new DuelAbilityRegistry(new[] { ability }));
        while (engine.State.Phase != DuelPhase.Main1) engine.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = engine.State.WaitingSeat });
        engine.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = 1, AbilityId = ability.AbilityId });
        engine.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 1 });
        engine.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 });
        var decision = Assert.IsType<DuelDecision>(engine.State.PendingDecision);
        Assert.True(engine.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0,
            DecisionId = decision.Id, Options = new[] { "choice" } }).Accepted);
        Assert.Equal(8500, engine.State.Players[0].LifePoints);
        Assert.Empty(engine.State.Chain);
    }

    sealed class DecisionProgram(DecisionKind kind, string expected) : IAbilityHandler
    {
        public string CardId => "70368879";
        public string AbilityId => "70368879.test-decision";
        public int Speed => 1;
        public bool CanActivate(EffectContext context) => true;
        public string ValidateActivation(EffectContext context, DuelCommand command) => "";
        public void PayCost(EffectContext context, DuelCommand command, DuelChainLink link) { }
        public void Resolve(EffectContext context, DuelChainLink link)
        {
            if (link.Step == 0)
            {
                link.Step = 1;
                context.State.PendingDecision = new DuelDecision { Id = context.State.NextDecisionId++, Player = 0,
                    Kind = kind, Min = 1, Max = 1, Continuation = AbilityId,
                    Options = new() { new DecisionOption { Id = "choice", Value = expected } } };
                context.State.Window = TimingWindow.Decision; context.State.WaitingSeat = 0;
                return;
            }
            if (link.AnswerKind == kind && link.Answers.SequenceEqual(new[] { expected })) context.Recover(0, 500);
            link.Step = 2;
        }
    }
}
