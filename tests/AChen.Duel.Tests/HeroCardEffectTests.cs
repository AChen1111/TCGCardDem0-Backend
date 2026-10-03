using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HeroCardEffectTests
{
    [Fact]
    public void CalledByTheGraveBanishesOnlyItsTargetWhileAnUnrelatedGraveMonsterStays()
    {
        var duel = Create(new[] { "24224830", "00213326", "89943723", "89943723", "89943723" },
            new[] { "14558127", "73642296", "89631139", "89631139", "89631139" });
        var source = duel.State.Cards.Single(card => card.DefinitionId == "24224830");
        var target = duel.State.Cards.Single(card => card.DefinitionId == "14558127");
        var unrelated = duel.State.Cards.Single(card => card.DefinitionId == "73642296");
        target.Zone = DuelZone.Graveyard; target.Position = CardPosition.FaceUp;
        unrelated.Zone = DuelZone.Graveyard; unrelated.Position = CardPosition.FaceUp;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = source.InstanceId, AbilityId = "24224830.1", TargetId = target.InstanceId }).Accepted);
        Resolve(duel);
        Assert.Equal(DuelZone.Banished, target.Zone);
        Assert.Equal(CardPosition.FaceUp, target.Position);
        Assert.Equal(DuelZone.Graveyard, unrelated.Zone);
        Assert.Equal(DuelZone.Graveyard, source.Zone);
    }

    [Fact]
    public void GhostBelleDiscardsItselfToNegateTheActivationBeforeTheGraveTargetIsBanished()
    {
        var duel = Create(new[] { "24224830", "00213326", "89943723", "89943723", "89943723" },
            new[] { "14558127", "73642296", "89631139", "89631139", "89631139" });
        var spell = duel.State.Cards.Single(card => card.DefinitionId == "24224830");
        var target = duel.State.Cards.Single(card => card.DefinitionId == "14558127");
        var belle = duel.State.Cards.Single(card => card.DefinitionId == "73642296");
        target.Zone = DuelZone.Graveyard; target.Position = CardPosition.FaceUp;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = spell.InstanceId, AbilityId = "24224830.1", TargetId = target.InstanceId }).Accepted);
        Assert.Contains(duel.QueryLegalActions(1), action => action.AbilityId == "73642296.1");
        var activation = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = belle.InstanceId, AbilityId = "73642296.1" });
        Assert.True(activation.Accepted);
        Assert.Contains(activation.Events, fact => fact.Kind == DuelEventKind.Moved
            && fact.DefinitionId == "73642296" && fact.Cause == MoveCause.Cost);
        Resolve(duel);
        Assert.Equal(DuelZone.Graveyard, target.Zone);
        Assert.Equal(DuelZone.Graveyard, belle.Zone);
        Assert.Empty(duel.State.Effects);
        Assert.Empty(duel.State.Chain);
    }

    [Fact]
    public void CalledByKeepsAnotherCopysActivationLegalButNegatesItsResponseEffect()
    {
        var duel = Create(new[] { "24224830", "00213326", "89943723", "89943723", "89943723" },
            new[] { "14558127", "14558127", "89631139", "89631139", "89631139" });
        var spell = duel.State.Cards.Single(card => card.DefinitionId == "24224830");
        var ashes = duel.State.Cards.Where(card => card.Owner == 1 && card.DefinitionId == "14558127").ToArray();
        ashes[0].Zone = DuelZone.Graveyard; ashes[0].Position = CardPosition.FaceUp;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = spell.InstanceId, AbilityId = "24224830.1", TargetId = ashes[0].InstanceId }).Accepted);
        Resolve(duel); Resolve(duel);
        var call = duel.State.Cards.Single(card => card.DefinitionId == "00213326");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = call.InstanceId, AbilityId = "00213326.1" }).Accepted);
        var activation = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = ashes[1].InstanceId, AbilityId = "14558127.1" });
        // 被无效的卡仍能发动并支付费用，但灰流丽本身的效果不能无效检索。
        Assert.True(activation.Accepted);
        Resolve(duel);
        Assert.NotNull(duel.State.PendingDecision);
        Assert.Equal(DecisionKind.ChooseCards, duel.State.PendingDecision.Kind);
        Assert.Equal(DuelZone.Graveyard, ashes[1].Zone);
    }

    [Fact]
    public void AHeroLivesPaysHalfLifeBeforeResponsesAndSummonsTheChosenElementalHero()
    {
        var duel = Create(new[] { "08949584", "00213326", "89943723", "89943723", "89943723" },
            Enumerable.Repeat("89631139", 5).ToArray());
        var card = duel.State.Cards.Single(state => state.DefinitionId == "08949584");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = card.InstanceId, AbilityId = "08949584.1" }).Accepted);
        Assert.Equal(4000, duel.State.Players[0].LifePoints);
        Resolve(duel);
        var decision = duel.State.PendingDecision;
        Assert.All(decision.Options, option => Assert.Equal("40044918", duel.State.Cards.Single(state => state.Ref.Equals(option.Card)).DefinitionId));
        Answer(duel, decision.Options[0].Id);
        Assert.Equal(DecisionKind.ChooseZone, duel.State.PendingDecision.Kind);
        Answer(duel, "2");
        Assert.Equal(DecisionKind.ChoosePosition, duel.State.PendingDecision.Kind);
        Answer(duel, "defense");
        var summoned = Assert.Single(duel.State.Cards.Where(state => state.Zone == DuelZone.Monster));
        Assert.Equal("40044918", summoned.DefinitionId);
        Assert.Equal(2, summoned.Slot);
        Assert.Equal(CardPosition.FaceUpDefense, summoned.Position);
        Assert.Equal(4000, duel.State.Players[0].LifePoints);
    }

    [Fact]
    public void MaliciousBanishesItselfAsCostAndSpecialSummonsAnotherCopyFromTheDeck()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "09411399", "89943723", "89943723", "89943723", "89943723" }
                .Concat(new[] { "09411399" }).Concat(Enumerable.Repeat("89631139", 34)).ToArray(),
                Enumerable.Repeat("89631139", 40).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Resolve(duel);
        var source = duel.State.Cards.First(card => card.DefinitionId == "09411399");
        source.Zone = DuelZone.Graveyard; source.Position = CardPosition.FaceUp;
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = source.InstanceId, AbilityId = "09411399.1" });
        Assert.True(result.Accepted);
        Assert.Equal(DuelZone.Banished, source.Zone);
        Assert.Contains(result.Events, fact => fact.DefinitionId == "09411399" && fact.Cause == MoveCause.Cost);
        Resolve(duel); Answer(duel, duel.State.PendingDecision.Options.Single().Id);
        Answer(duel, "1"); Answer(duel, "attack");
        var summoned = Assert.Single(duel.State.Cards.Where(card => card.Zone == DuelZone.Monster));
        Assert.Equal("09411399", summoned.DefinitionId);
        Assert.NotEqual(source.InstanceId, summoned.InstanceId);
    }

    [Fact]
    public void CrossoutRequiresADeclarableNameInTheDeckAndBanishesTheChosenCopy()
    {
        var duel = Create(new[] { "65681983", "89943723", "89943723", "89943723", "89943723" },
            Enumerable.Repeat("89631139", 5).ToArray());
        var crossout = duel.State.Cards.Single(card => card.DefinitionId == "65681983");
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = crossout.InstanceId, AbilityId = "65681983.1", NameId = "46986414" }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = crossout.InstanceId, AbilityId = "65681983.1", NameId = "40044918" }).Accepted);
        Resolve(duel);
        var option = duel.State.PendingDecision.Options[0];
        var selected = duel.State.Cards.Single(card => card.Ref.Equals(option.Card));
        Answer(duel, option.Id);
        Assert.Equal(DuelZone.Banished, selected.Zone);
        Assert.Equal(CardPosition.FaceUp, selected.Position);
        Assert.Single(duel.State.Cards.Where(card => card.Zone == DuelZone.Banished));
    }

    [Fact]
    public void DropletPaysTheSelectedCardsBeforeResponsesAndHalvesChosenEffectMonsters()
    {
        var duel = Create(new[] { "24299458", "89943723", "89943723", "89943723", "89943723" },
            new[] { "27780618", "73642296", "89631139", "89631139", "89631139" });
        var droplet = duel.State.Cards.Single(card => card.DefinitionId == "24299458");
        var cost = duel.State.Cards.First(card => card.Owner == 0 && card.Zone == DuelZone.Hand && card.DefinitionId == "89943723");
        var vyon = duel.State.Cards.Single(card => card.DefinitionId == "27780618");
        vyon.Zone = DuelZone.Monster; vyon.Slot = 0; vyon.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = droplet.InstanceId, AbilityId = "24299458.1", Cards = new[] { cost.InstanceId } }).Accepted);
        Assert.Equal(DuelZone.Graveyard, cost.Zone);
        Assert.DoesNotContain(duel.QueryLegalActions(1), action => action.Kind == DuelCommandKind.Activate);
        Resolve(duel); Answer(duel, vyon.InstanceId.ToString());
        Assert.Equal(500, vyon.CurrentAtk);
        Assert.True(vyon.Negated);
        Assert.Equal(DuelZone.Graveyard, droplet.Zone);
    }

    [Fact]
    public void VyonBanishesAHeroAsCostToSearchPolymerizationWithAnInstanceLimit()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "27780618", "27780618", "89943723", "89943723", "89943723" }
                .Concat(new[] { "24094653", "24094653" }).Concat(Enumerable.Repeat("89631139", 33)).ToArray(),
                Enumerable.Repeat("89631139", 40).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Resolve(duel);
        var vyons = duel.State.Cards.Where(card => card.DefinitionId == "27780618").ToArray();
        var costs = duel.State.Cards.Where(card => card.Owner == 0 && card.DefinitionId == "89943723").Take(2).ToArray();
        for (int index = 0; index < 2; index++)
        { vyons[index].Zone = DuelZone.Monster; vyons[index].Position = CardPosition.FaceUpAttack; vyons[index].Slot = index;
            costs[index].Zone = DuelZone.Graveyard; costs[index].Position = CardPosition.FaceUp; }
        for (int index = 0; index < 2; index++)
        {
            Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
                CardId = vyons[index].InstanceId, AbilityId = "27780618.2", Cards = new[] { costs[index].InstanceId } }).Accepted);
            Assert.Equal(DuelZone.Banished, costs[index].Zone);
            Resolve(duel); Answer(duel, duel.State.PendingDecision.Options[0].Id); Resolve(duel);
        }
        Assert.Equal(2, duel.State.Cards.Count(card => card.DefinitionId == "24094653" && card.Zone == DuelZone.Hand));
    }

    [Fact]
    public void BlazemanCopiesTheSentElementalHerosAttributesAndLocksFurtherSpecialSummonsToFusion()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "63060238", "09411399", "89943723", "89943723", "89943723" }
                .Concat(new[] { "89943723", "09411399" }).Concat(Enumerable.Repeat("89631139", 33)).ToArray(),
                Enumerable.Repeat("89631139", 40).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Resolve(duel);
        var blaze = duel.State.Cards.Single(card => card.DefinitionId == "63060238");
        blaze.Zone = DuelZone.Monster; blaze.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = blaze.InstanceId, AbilityId = "63060238.2" }).Accepted);
        Resolve(duel); Answer(duel, duel.State.PendingDecision.Options.Single().Id);
        Assert.Equal(16, blaze.CurrentAttribute);
        Assert.Equal(2500, blaze.CurrentAtk);
        Assert.Equal(2000, blaze.CurrentDef);
        var malicious = duel.State.Cards.First(card => card.DefinitionId == "09411399");
        malicious.Zone = DuelZone.Graveyard; malicious.Position = CardPosition.FaceUp;
        Resolve(duel);
        Assert.DoesNotContain(duel.QueryLegalActions(0), action => action.AbilityId == "09411399.1");
    }

    [Fact]
    public void BlazemansOwnCopiedAttackAndDefenseEndWhenVeilerNegatesItsMonsterEffect()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { new[] { "63060238", "26077389", "26077389", "26077389", "26077389" }
                .Concat(Enumerable.Repeat("89943723", 35)).ToArray(), new[] { "97268402" }.Concat(Enumerable.Repeat("89631139", 39)).ToArray() } });
        while (duel.State.Phase != DuelPhase.Main1) Resolve(duel);
        var blaze = duel.State.Cards.Single(card => card.DefinitionId == "63060238");
        blaze.Zone = DuelZone.Monster; blaze.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = blaze.InstanceId, AbilityId = "63060238.2" }).Accepted);
        Resolve(duel); Answer(duel, duel.State.PendingDecision.Options[0].Id);
        Assert.Equal(2500, blaze.CurrentAtk);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 }).Accepted);
        var veiler = duel.State.Cards.Single(card => card.DefinitionId == "97268402");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1, CardId = veiler.InstanceId,
            AbilityId = "97268402.1", TargetId = blaze.InstanceId }).Accepted);
        Resolve(duel);
        Assert.True(blaze.Negated);
        Assert.Equal(1200, blaze.CurrentAtk);
        Assert.Equal(1800, blaze.CurrentDef);
    }

    [Fact]
    public void WakeDoesNotRegainItsSummonAttackBonusWhenVeilersTurnExpires()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(),
                new[] { "97268402" }.Concat(Enumerable.Repeat("89631139", 39)).ToArray() },
            ExtraDecks = new[] { new[] { "32828466" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Resolve(duel);
        var wake = duel.State.Cards.Single(card => card.DefinitionId == "32828466");
        wake.Zone = DuelZone.Monster; wake.Position = CardPosition.FaceUpAttack;
        wake.SummonMaterialDefinitions.AddRange(new[] { "22908820", "23204029", "89943723" });
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 }).Accepted);
        Assert.Equal(3400, wake.CurrentAtk);
        var veiler = duel.State.Cards.Single(card => card.DefinitionId == "97268402");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1, CardId = veiler.InstanceId,
            AbilityId = "97268402.1", TargetId = wake.InstanceId }).Accepted);
        Resolve(duel); Assert.Equal(2500, wake.CurrentAtk);
        Resolve(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        for (int step = 0; duel.State.Turn < 2 && step < 16; step++)
            Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
        Assert.Equal(2, duel.State.Turn);
        Assert.False(wake.Negated);
        Assert.Equal(2500, wake.CurrentAtk);
        Assert.True(wake.SummonModifiersSuppressed);
    }

    [Fact]
    public void DrollOpensAfterAnOpponentsSearchAndPreventsTheNextMandatoryDeckSearch()
    {
        var duel = Create(new[] { "00213326", "00213326", "89943723", "89943723", "89943723" },
            new[] { "94145021", "89631139", "89631139", "89631139", "89631139" });
        var calls = duel.State.Cards.Where(card => card.DefinitionId == "00213326").ToArray();
        var droll = duel.State.Cards.Single(card => card.DefinitionId == "94145021");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = calls[0].InstanceId, AbilityId = "00213326.1" }).Accepted);
        Resolve(duel); Answer(duel, duel.State.PendingDecision.Options[0].Id);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = 0 }).Accepted);
        Assert.Contains(duel.QueryLegalActions(1), action => action.AbilityId == "94145021.1");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = droll.InstanceId, AbilityId = "94145021.1" }).Accepted);
        Resolve(duel); Resolve(duel);
        Assert.Equal(DuelZone.Graveyard, droll.Zone);
        Assert.DoesNotContain(duel.QueryLegalActions(0), action => action.AbilityId == "00213326.1");
    }

    [Fact]
    public void FuwalosDrawsOnlyAfterTheOpponentsDeckSpecialSummonCompletes()
    {
        var duel = Create(new[] { "08949584", "00213326", "89943723", "89943723", "89943723" },
            new[] { "42141493", "89631139", "89631139", "89631139", "89631139" });
        var lives = duel.State.Cards.Single(card => card.DefinitionId == "08949584");
        var bird = duel.State.Cards.Single(card => card.DefinitionId == "42141493");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = lives.InstanceId, AbilityId = "08949584.1" }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = bird.InstanceId, AbilityId = "42141493.1" }).Accepted);
        Assert.Equal(4, duel.State.Cards.Count(card => card.Owner == 1 && card.Zone == DuelZone.Hand));
        Resolve(duel);
        Answer(duel, duel.State.PendingDecision.Options[0].Id);
        Answer(duel, "0"); Answer(duel, "attack");
        Assert.Equal(5, duel.State.Cards.Count(card => card.Owner == 1 && card.Zone == DuelZone.Hand));
        Assert.Equal(DuelZone.Graveyard, bird.Zone);
    }

    [Fact]
    public void ContrastHeroKeepsBothAttributesAndNegatesAFaceUpCardForTheTurn()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord { Shuffle = false,
            MainDecks = new[] { Enumerable.Repeat("89943723", 40).ToArray(),
                new[] { "40044918" }.Concat(Enumerable.Repeat("89631139", 39)).ToArray() },
            ExtraDecks = new[] { new[] { "23204029" }, Array.Empty<string>() } });
        while (duel.State.Phase != DuelPhase.Main1) Resolve(duel);
        var chaos = duel.State.Cards.Single(card => card.DefinitionId == "23204029");
        chaos.Zone = DuelZone.Monster; chaos.Position = CardPosition.FaceUpAttack;
        var target = duel.State.Cards.Single(card => card.DefinitionId == "40044918");
        target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = chaos.InstanceId, AbilityId = "23204029.2", TargetId = target.InstanceId }).Accepted);
        Resolve(duel);
        Assert.Equal(48, chaos.CurrentAttribute);
        Assert.True(target.Negated);
        Resolve(duel);
        Assert.DoesNotContain(duel.QueryLegalActions(0), action => action.AbilityId == "23204029.2");
    }

    static void Answer(DuelEngine duel, string option)
    {
        var choice = duel.State.PendingDecision;
        var answer = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = choice.Player,
            DecisionId = choice.Id, Options = new[] { option } });
        Assert.True(answer.Accepted, answer.Error);
    }

    static void Resolve(DuelEngine duel)
    {
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
    }
    static DuelEngine Create(string[] hand0, string[] hand1)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord {
            Shuffle = false, MainDecks = new[] { hand0.Concat(Enumerable.Repeat("40044918", 35)).ToArray(),
                hand1.Concat(Enumerable.Repeat("89631139", 35)).ToArray() }
        });
        while (duel.State.Phase != DuelPhase.Main1)
            duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat });
        return duel;
    }
}
