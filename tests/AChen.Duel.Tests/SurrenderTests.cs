using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class SurrenderTests
{
    [Fact]
    public void NonRespondingPlayerCanSurrenderThroughAnOfferedAction()
    {
        var engine = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { MainDecks = new[] {
            Enumerable.Repeat("89631139", 40).ToArray(), Enumerable.Repeat("89631139", 40).ToArray() } });
        var action = Assert.Single(engine.QueryLegalActions(1).Where(a => a.Kind == DuelCommandKind.Surrender));
        Assert.True(engine.Apply(new DuelCommand { Kind = action.Kind, Player = 1 }).Accepted);
        Assert.Equal(0, engine.State.Winner);
    }
}
