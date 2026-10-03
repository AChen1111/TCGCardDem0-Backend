using System.Text.Json;
using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class ProjectionEventTests
{
    [Fact]
    public void BriefPublicRevealSurvivesConcealmentButPrivateDrawIdentityNeverReachesOpponent()
    {
        var events = new[]
        {
            new DuelEvent { Id = 1, Kind = DuelEventKind.Revealed, Player = 1, HasCard = true,
                Card = new CardRef(73, 1), DefinitionId = "briefly-public", VisibleToMask = 3,
                From = DuelZone.Hand, To = DuelZone.Deck, Attack = 2400, Defense = 1800 },
            new DuelEvent { Id = 2, Kind = DuelEventKind.Drawn, Player = 1, HasCard = true,
                Card = new CardRef(99, 2), DefinitionId = "draw-secret", VisibleToMask = 2,
                From = DuelZone.Deck, To = DuelZone.Hand, Attack = 1700, Defense = 1000,
                Detail = "internal draw-secret card-99" }
        };
        // The current state no longer contains the revealed card; facts are projected at event time.
        var projector = new SeatProjection(0);
        projector.Project(new DuelState());
        var projected = projector.ProjectEvents(events);
        Assert.Equal("briefly-public", projected[0].DefinitionId);
        Assert.Equal(2400, projected[0].Attack);
        Assert.Equal("", projected[1].DefinitionId);
        Assert.Equal("", projected[1].ViewCardId);
        Assert.Null(projected[1].Attack);
        Assert.Null(projected[1].Defense);
        string wire = JsonSerializer.Serialize(projected);
        Assert.DoesNotContain("draw-secret", wire);
        Assert.DoesNotContain("InstanceId", wire);
        Assert.DoesNotContain("Generation", wire);
        Assert.Equal("draw-secret", new SeatProjection(1).ProjectEvents(events)[1].DefinitionId);
    }
}
