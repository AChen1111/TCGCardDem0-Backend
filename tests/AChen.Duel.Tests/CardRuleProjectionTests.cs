using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class CardRuleProjectionTests
{
    [Fact]
    public void CrossoutCanBeSubmittedUsingOnlyItsProjectedActionTokenAndPublicNameDeclaration()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "65681983", "89943723", "89943723", "89943723", "89943723" }
                .Concat(Enumerable.Repeat("40044918", 35)).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var projection = new SeatProjection(0);
        var view = projection.Project(duel.State, duel.QueryLegalActions(0));
        var action = view.Actions.Single(offered => offered.AbilityId == "65681983.1");
        Assert.True(action.RequiresNameDeclaration);
        Assert.Equal((RuleCardKind)0, action.DeclarationKind);
        Assert.True(projection.TryResolveInput(duel.State, new SeatInput { Revision = view.Revision,
            ActionToken = action.ActionToken, NameId = "40044918", Slot = action.Slots[0] }, out var command));
        var applied = duel.Apply(command);
        Assert.True(applied.Accepted, applied.Error);
        Assert.Contains(applied.Events, fact => fact.Kind == DuelEventKind.Activated && fact.DeclaredNameId == "40044918");
        Pass(duel); Pass(duel);
        view = projection.Project(duel.State, duel.QueryLegalActions(0));
        Assert.NotNull(view.Decision);
        Assert.True(projection.TryResolveInput(duel.State, new SeatInput { Revision = view.Revision,
            OptionTokens = new[] { view.Decision.Options[0].OptionToken } }, out command));
        Assert.True(duel.Apply(command).Accepted);
        Assert.Single(duel.State.Cards.Where(card => card.Owner == 0 && card.Zone == DuelZone.Banished));
    }
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
}
