using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class NormalSpellEffectTests
{
    [Fact]
    public void EmergencyCallSelectsAnElementalHeroAtResolutionThenRevealsAndAddsIt()
    {
        var engine = Create("00213326", "89631139", "89943723");
        var source = engine.State.Cards.Single(card => card.DefinitionId == "00213326");
        var context = new EffectContext(engine, 0, source.InstanceId);
        var handler = DuelAbilityRegistry.CreateDefault().Get("00213326.1");
        var link = new DuelChainLink { Player = 0, Source = source.Ref, AbilityId = handler.AbilityId };

        Assert.True(handler.CanActivate(context));
        handler.Resolve(context, link);

        var decision = engine.State.PendingDecision;
        Assert.NotNull(decision);
        Assert.Equal(0, decision.Player);
        Assert.Equal(DecisionKind.ChooseCards, decision.Kind);
        var option = Assert.Single(decision.Options);
        Assert.Equal("89943723", engine.State.Cards.Single(card => card.InstanceId == option.Card.InstanceId).DefinitionId);
        Assert.DoesNotContain(engine.State.Cards, card => card.DefinitionId == "89943723" && card.Zone == DuelZone.Hand);
        link.Selected.Add(option.Card.InstanceId);
        engine.State.PendingDecision = null;
        handler.Resolve(context, link);

        Assert.Contains(engine.State.Cards, card => card.DefinitionId == "89943723" && card.Zone == DuelZone.Hand);
        Assert.Null(engine.State.PendingDecision);
    }

    [Fact]
    public void ReinforcementOnlyOffersWarriorsOfLevelFourOrLower()
    {
        var engine = Create("32807848", "89943723", "89631139", "94145021", "26077389");
        var source = engine.State.Cards.Single(card => card.DefinitionId == "32807848");
        var handler = DuelAbilityRegistry.CreateDefault().Get("32807848.1");
        var context = new EffectContext(engine, 0, source.InstanceId);
        handler.Resolve(context, new DuelChainLink { Player = 0, Source = source.Ref, AbilityId = handler.AbilityId });
        var option = Assert.Single(engine.State.PendingDecision.Options);
        Assert.Equal("26077389", engine.State.Cards.Single(card => card.InstanceId == option.Card.InstanceId).DefinitionId);
    }

    [Fact]
    public void FoolishBurialSendsAChosenMonsterWithoutTreatingItAsAnActivationCost()
    {
        var engine = Create("81439173", "00213326", "71039903");
        var source = engine.State.Cards.Single(card => card.DefinitionId == "81439173");
        var stone = engine.State.Cards.Single(card => card.DefinitionId == "71039903");
        var handler = DuelAbilityRegistry.CreateDefault().Get("81439173.1");
        var context = new EffectContext(engine, 0, source.InstanceId);
        var link = new DuelChainLink { Player = 0, Source = source.Ref, AbilityId = handler.AbilityId };
        handler.PayCost(context, new DuelCommand(), link);
        Assert.Equal(DuelZone.Deck, stone.Zone);
        handler.Resolve(context, link);
        Assert.Equal(stone.Ref, Assert.Single(engine.State.PendingDecision.Options).Card);
        link.Selected.Add(stone.InstanceId);
        engine.State.PendingDecision = null;
        handler.Resolve(context, link);
        Assert.Equal(DuelZone.Graveyard, stone.Zone);
        Assert.Empty(link.Costs);
    }

    [Fact]
    public void UpstartDrawsOneAndRecoversTheOpponentsLifeOnlyOnce()
    {
        var engine = Create("70368879", "00213326", "71039903");
        var source = engine.State.Cards.Single(card => card.DefinitionId == "70368879");
        var handler = DuelAbilityRegistry.CreateDefault().Get("70368879.1");
        var context = new EffectContext(engine, 0, source.InstanceId);
        var link = new DuelChainLink { Player = 0, Source = source.Ref, AbilityId = handler.AbilityId };
        Assert.True(handler.CanActivate(context));
        handler.Resolve(context, link);
        handler.Resolve(context, link);
        Assert.Equal(2, engine.State.Cards.Count(card => card.Owner == 0 && card.Zone == DuelZone.Hand));
        Assert.Equal(9000, engine.State.Players[1].LifePoints);
        Assert.Equal(8000, engine.State.Players[0].LifePoints);
    }

    [Fact]
    public void TradeInRequiresDiscardingALevelEightBeforeItsDrawResolves()
    {
        var engine = CreateWithHand(2, "38120068", "89631139", "00213326", "71039903");
        var source = engine.State.Cards.Single(card => card.DefinitionId == "38120068");
        var dragon = engine.State.Cards.Single(card => card.Owner == 0 && card.DefinitionId == "89631139");
        var handler = DuelAbilityRegistry.CreateDefault().Get("38120068.1");
        var context = new EffectContext(engine, 0, source.InstanceId);
        var link = new DuelChainLink { Player = 0, Source = source.Ref, AbilityId = handler.AbilityId };
        var command = new DuelCommand { Cards = new[] { dragon.InstanceId } };
        Assert.True(handler.CanActivate(context));
        Assert.Equal("", handler.ValidateActivation(context, command));
        Assert.NotEqual("", handler.ValidateActivation(context, new DuelCommand { Cards = new[] { source.InstanceId } }));
        handler.PayCost(context, command, link);
        Assert.Equal(DuelZone.Graveyard, dragon.Zone);
        Assert.Single(link.Costs);
        Assert.Single(engine.State.Cards.Where(card => card.Owner == 0 && card.Zone == DuelZone.Hand));
        handler.Resolve(context, link);
        Assert.Equal(3, engine.State.Cards.Count(card => card.Owner == 0 && card.Zone == DuelZone.Hand));
    }

    [Fact]
    public void ConsonanceRejectsNonDragonTunersAndAcceptsTheWhiteStone()
    {
        var engine = CreateWithHand(3, "39701395", "14558127", "71039903", "00213326", "89943723");
        var source = engine.State.Cards.Single(card => card.DefinitionId == "39701395");
        var stone = engine.State.Cards.Single(card => card.DefinitionId == "71039903");
        var ash = engine.State.Cards.Single(card => card.DefinitionId == "14558127");
        var handler = DuelAbilityRegistry.CreateDefault().Get("39701395.1");
        var context = new EffectContext(engine, 0, source.InstanceId);
        var costs = Assert.IsAssignableFrom<IActivationCostSelection>(handler);
        Assert.Equal(stone.InstanceId, Assert.Single(costs.CostCandidates(context)).InstanceId);
        Assert.NotEqual("", handler.ValidateActivation(context, new DuelCommand { Cards = new[] { ash.InstanceId } }));
        var command = new DuelCommand { Cards = new[] { stone.InstanceId } };
        var link = new DuelChainLink { Player = 0, Source = source.Ref, AbilityId = handler.AbilityId };
        Assert.Equal("", handler.ValidateActivation(context, command));
        handler.PayCost(context, command, link);
        handler.Resolve(context, link);
        Assert.Equal(DuelZone.Graveyard, stone.Zone);
        Assert.Equal(4, engine.State.Cards.Count(card => card.Owner == 0 && card.Zone == DuelZone.Hand));
    }

    [Fact]
    public void DesiresBanishesTheTopTenFaceDownAsCostBeforeDrawingTwo()
    {
        var engine = Create(new[] { "35261759" }.Concat(Enumerable.Repeat("89631139", 12)).ToArray());
        var source = engine.State.Cards.Single(card => card.DefinitionId == "35261759");
        var handler = DuelAbilityRegistry.CreateDefault().Get("35261759.1");
        var context = new EffectContext(engine, 0, source.InstanceId);
        var link = new DuelChainLink { Player = 0, Source = source.Ref, AbilityId = handler.AbilityId };
        var topTen = engine.State.Players[0].Deck.Take(10).ToArray();
        Assert.True(handler.CanActivate(context));
        handler.PayCost(context, new DuelCommand(), link);
        Assert.Equal(2, engine.State.Players[0].Deck.Count);
        Assert.Equal(10, link.Costs.Count);
        Assert.All(topTen, id => {
            var card = engine.State.Cards.Single(card => card.InstanceId == id);
            Assert.Equal(DuelZone.Banished, card.Zone);
            Assert.Equal(CardPosition.FaceDown, card.Position);
        });
        handler.Resolve(context, link);
        Assert.Empty(engine.State.Players[0].Deck);
        Assert.Equal(3, engine.State.Cards.Count(card => card.Owner == 0 && card.Zone == DuelZone.Hand));
    }

    static DuelEngine Create(params string[] deck) => CreateWithHand(1, deck);
    static DuelEngine CreateWithHand(int hand, params string[] deck) => new(DuelCardCatalog.CreateDefault(), new DuelStartRecord
    {
        MainDecks = new[] { deck, new[] { "89631139", "89943723", "89631139" } },
        OpeningHand = hand, Shuffle = false
    });
}
