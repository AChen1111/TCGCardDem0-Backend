using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class SkyCardEffectTests
{
    [Fact]
    public void LemnisGateRejectsUnequalActivationTargetGroupsWithoutMovingTheSpell()
    {
        var duel = Create("34433770", "26077389", "26077389", "63166096");
        foreach (var card in duel.State.Cards.Where(c => c.DefinitionId != "34433770"))
        { card.Zone = DuelZone.Graveyard; duel.State.Players[0].Deck.Remove(card.InstanceId); }
        var source = duel.State.Cards.Single(c => c.DefinitionId == "34433770");
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = source.InstanceId, AbilityId = "34433770.1",
            Cards = duel.State.Cards.Where(c => c.DefinitionId == "26077389").Select(c => c.InstanceId).ToArray() });
        Assert.False(result.Accepted); Assert.Equal("INVALID_TARGET_GROUP", result.Error);
        Assert.Equal(DuelZone.Hand, source.Zone); Assert.Empty(duel.State.Chain);
    }

    [Fact]
    public void BlackGoatGraveyardEffectBanishesItselfAndBlocksDeclaredOriginalFieldEffects()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "49299410", "38517737" }, new[] { "26077389" } }, OpeningHand = 0, Shuffle = false
        });
        foreach (var card in duel.State.Cards)
        { duel.State.Players[card.Owner].Deck.Clear(); card.Zone = card.DefinitionId == "49299410" ? DuelZone.Graveyard : DuelZone.Monster;
            card.Position = CardPosition.FaceUpAttack; }
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        var goat = duel.State.Cards.Single(c => c.DefinitionId == "49299410");
        var alternative = duel.State.Cards.Single(c => c.DefinitionId == "38517737");
        Assert.Contains(duel.QueryLegalActions(0), action => action.AbilityId == "38517737.2");
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = goat.InstanceId,
            AbilityId = "49299410.2", NameId = "38517737" });
        Assert.True(result.Accepted, result.Error); Assert.Equal(DuelZone.Banished, goat.Zone);
        ResolveToDecision(duel);
        Assert.Equal("89631139", alternative.CurrentNameId); // Effective name changes do not bypass the original-name declaration.
        Assert.DoesNotContain(duel.QueryLegalActions(0), action => action.AbilityId == "38517737.2");
    }

    [Fact]
    public void LemnisGateCanBounceAFieldCardAfterReturningFourGraveyardTargets()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "34433770", "26077389", "37351133", "63166096", "98338152" }, new[] { "89631139" } },
            OpeningHand = 0, Shuffle = false
        });
        foreach (var card in duel.State.Cards)
        { duel.State.Players[card.Owner].Deck.Clear(); card.Zone = card.Owner == 1 ? DuelZone.Monster : card.DefinitionId == "34433770" ? DuelZone.Hand : DuelZone.Graveyard;
            card.Position = CardPosition.FaceUpAttack; }
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        var source = duel.State.Cards.Single(c => c.DefinitionId == "34433770");
        var target = duel.State.Cards.Single(c => c.Owner == 1);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = source.InstanceId,
            AbilityId = "34433770.1", Cards = duel.State.Cards.Where(c => c.Zone == DuelZone.Graveyard).Select(c => c.InstanceId).ToArray() }).Accepted);
        ResolveToDecision(duel);
        var choice = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal(0, choice.Min); Assert.Equal(1, choice.Max);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0, DecisionId = choice.Id,
            Options = new[] { choice.Options.Single(o => o.Card.Equals(target.Ref)).Id } }).Accepted);
        Assert.Equal(DuelZone.Hand, target.Zone); Assert.Equal(4, duel.State.Players[0].Deck.Count);
    }

    [Fact]
    public void LemnisGateGraveyardTriggerBanishesItselfAndPerformsAProperLinkSummon()
    {
        var summon = new ProgramAbility("26077389", 99, 1, new[] { DuelZone.Hand }, c => true,
            (c, link) => c.SpecialSummon(c.Source, c.Player, 0, CardPosition.FaceUpAttack));
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389", "34433770" }, Array.Empty<string>() },
            ExtraDecks = new[] { new[] { "12421694" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(DuelAbilityRegistry.CreateDefault().Handlers.Concat(new[] { summon })));
        var raye = duel.State.Cards.Single(c => c.DefinitionId == "26077389"); raye.Zone = DuelZone.Hand;
        var spell = duel.State.Cards.Single(c => c.DefinitionId == "34433770"); spell.Zone = DuelZone.Graveyard;
        duel.State.Players[0].Deck.Clear(); duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Activate(duel, "26077389", "26077389.99"); ResolveToDecision(duel); AnswerFirst(duel);
        Assert.Equal(DuelZone.Banished, spell.Zone);
        ResolveToDecision(duel); AnswerFirst(duel); AnswerFirst(duel); AnswerFirst(duel);
        var target = duel.State.Cards.Single(c => c.DefinitionId == "12421694");
        Assert.Equal(DuelZone.ExtraMonster, target.Zone);
        Assert.True(target.ProperlySummoned); Assert.Equal(SummonMethod.Link, target.SummonMethod);
        Assert.Equal(DuelZone.Graveyard, raye.Zone);
    }

    [Fact]
    public void LemnisGateTargetsEqualNumbersOfGraveyardStrikerMonstersAndSpellsWithoutPayingThemAsCosts()
    {
        var duel = Create("34433770", "26077389", "63166096");
        foreach (var card in duel.State.Cards.Where(c => c.DefinitionId != "34433770"))
        { card.Zone = DuelZone.Graveyard; duel.State.Players[0].Deck.Remove(card.InstanceId); }
        var source = duel.State.Cards.Single(c => c.DefinitionId == "34433770");
        var targets = duel.State.Cards.Where(c => c.Zone == DuelZone.Graveyard).ToArray();
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = source.InstanceId, AbilityId = "34433770.1", Cards = targets.Select(c => c.InstanceId).ToArray() });
        Assert.True(result.Accepted, result.Error);
        Assert.All(targets, c => Assert.Equal(DuelZone.Graveyard, c.Zone));
        Assert.Empty(duel.State.Chain.Single().Costs); Assert.Equal(2, duel.State.Chain.Single().Targets.Count);
        ResolveToDecision(duel);
        Assert.All(targets, c => Assert.Equal(DuelZone.Deck, c.Zone));
        Assert.Null(duel.State.PendingDecision);
    }

    [Fact]
    public void AmatsuBattleTriggerTargetsOneOwnStrikerAndOneOpponentCardWithoutPayingCardCosts()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { Array.Empty<string>(), new[] { "89631139" } },
            ExtraDecks = new[] { new[] { "25072579" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var source = duel.State.Cards.Single(c => c.DefinitionId == "25072579");
        source.Zone = DuelZone.ExtraMonster; source.Position = CardPosition.FaceUpAttack; duel.State.Players[0].ExtraDeck.Clear();
        var target = duel.State.Cards.Single(c => c.Owner == 1);
        target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack; duel.State.Players[1].Deck.Clear();
        duel.State.Turn = 2; duel.State.Phase = DuelPhase.Battle; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0,
            CardId = source.InstanceId, TargetId = target.InstanceId }).Accepted);
        AnswerFirst(duel);
        var choices = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal(2, choices.Min);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = choices.Player,
            DecisionId = choices.Id, Options = choices.Options.Select(o => o.Id).ToArray() });
        Assert.True(result.Accepted, result.Error);
        Assert.Empty(duel.State.Chain.Single().Costs);
        Assert.Equal(2, duel.State.Chain.Single().Targets.Count);
        ResolveToDecision(duel);
        Assert.Equal(DuelZone.Graveyard, source.Zone);
        Assert.Equal(DuelZone.Graveyard, target.Zone);
        Assert.Equal(8000, duel.State.Players[0].LifePoints);
    }

    [Fact]
    public void AmatsuRewritesAnOpponentHighAttackMonsterEffectAndOriginalCasterChoosesTheLinkToDestroy()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389" }, new[] { "38517737" } },
            ExtraDecks = new[] { new[] { "25072579" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false, FirstPlayer = 1
        });
        foreach (var card in duel.State.Cards)
        {
            duel.State.Players[card.Owner].Deck.Clear(); duel.State.Players[card.Owner].ExtraDeck.Clear();
            card.Zone = card.DefinitionId == "25072579" ? DuelZone.ExtraMonster : DuelZone.Monster;
            card.Position = CardPosition.FaceUpAttack;
        }
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        var raye = duel.State.Cards.Single(c => c.DefinitionId == "26077389");
        var amatsu = duel.State.Cards.Single(c => c.DefinitionId == "25072579");
        Activate(duel, "38517737", "38517737.2", raye.InstanceId);
        Activate(duel, "25072579", "25072579.1"); ResolveToDecision(duel);
        var choice = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal(1, choice.Player);
        Assert.Equal(amatsu.Ref, Assert.Single(choice.Options).Card);
        AnswerFirst(duel);
        Assert.Equal(DuelZone.Graveyard, amatsu.Zone);
        Assert.Equal(DuelZone.Monster, raye.Zone);
        Assert.Empty(duel.State.Chain);
    }

    [Fact]
    public void ZeroTributesItselfSummonsRayeAndRozeAsOneGroupThenCanDestroyAFieldCard()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389", "37351133" }, new[] { "89631139", "70368879", "70368879" } },
            ExtraDecks = new[] { new[] { "76072561" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var source = duel.State.Cards.Single(c => c.DefinitionId == "76072561");
        source.Zone = DuelZone.ExtraMonster; source.Position = CardPosition.FaceUpAttack; duel.State.Players[0].ExtraDeck.Clear();
        var target = duel.State.Cards.Single(c => c.DefinitionId == "89631139");
        target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack; duel.State.Players[1].Deck.Remove(target.InstanceId);
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.DrawOnOpponentSpecialSummon, Player = 1, Value = 1 });
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Activate(duel, "76072561", "76072561.2"); Assert.Equal(DuelZone.Graveyard, source.Zone);
        ResolveToDecision(duel);
        var pair = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0,
            DecisionId = pair.Id, Options = pair.Options.Select(option => option.Id).ToArray() }).Accepted);
        AnswerFirst(duel); AnswerFirst(duel); AnswerFirst(duel); AnswerFirst(duel);
        Assert.All(duel.State.Cards.Where(c => c.Owner == 0 && c.DefinitionId != "76072561"), c => Assert.Equal(DuelZone.Monster, c.Zone));
        AnswerFirst(duel);
        var field = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0, DecisionId = field.Id,
            Options = new[] { field.Options.Single(option => option.Card.Equals(target.Ref)).Id } }).Accepted);
        Assert.Equal(DuelZone.Graveyard, target.Zone);
        Assert.Single(duel.State.Cards.Where(c => c.Owner == 1 && c.Zone == DuelZone.Hand));
    }

    [Fact]
    public void ZeroSearchesAStrikerSpellAfterSummonAndConsumesItsSharedEffectAllowance()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389", "37351133", "63166096" }, Array.Empty<string>() },
            ExtraDecks = new[] { new[] { "76072561" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var materials = duel.State.Cards.Where(c => c.DefinitionId == "26077389" || c.DefinitionId == "37351133").ToArray();
        foreach (var card in materials)
        { duel.State.Players[0].Deck.Remove(card.InstanceId); card.Zone = DuelZone.Monster; card.Slot = card.DefinitionId == "37351133" ? 1 : 0; card.Position = CardPosition.FaceUpAttack; }
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        var source = duel.State.Cards.Single(c => c.DefinitionId == "76072561");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = source.InstanceId, Cards = materials.Select(c => c.InstanceId).ToArray(), Slot = 5 }).Accepted);
        AnswerFirst(duel); ResolveToDecision(duel); AnswerFirst(duel); ResolveToDecision(duel);
        Assert.Contains(duel.State.Cards, c => c.DefinitionId == "63166096" && c.Zone == DuelZone.Hand);
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.AbilityId == "76072561.2");
    }

    [Fact]
    public void VeilerPermanentlyCancelsZekesPreviouslyResolvedSelfAttackIncrease()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "70368879" }, new[] { "97268402", "70368879" } },
            ExtraDecks = new[] { new[] { "75147529" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var target = duel.State.Cards.Single(c => c.Owner == 0 && c.DefinitionId == "70368879");
        target.Zone = DuelZone.SpellTrap; target.Position = CardPosition.FaceDown; duel.State.Players[0].Deck.Clear();
        var source = duel.State.Cards.Single(c => c.DefinitionId == "75147529");
        source.Zone = DuelZone.ExtraMonster; source.Position = CardPosition.FaceUpAttack; duel.State.Players[0].ExtraDeck.Clear();
        var veiler = duel.State.Cards.Single(c => c.DefinitionId == "97268402");
        veiler.Zone = DuelZone.Hand; duel.State.Players[1].Deck.Remove(veiler.InstanceId);
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Activate(duel, "75147529", "75147529.2", target.InstanceId); ResolveToDecision(duel);
        Assert.Equal(2500, source.CurrentAtk);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        Activate(duel, "97268402", "97268402.1", source.InstanceId); ResolveToDecision(duel);
        Assert.Equal(1500, source.CurrentAtk); Assert.True(source.Negated);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        Pass(duel); Pass(duel); Pass(duel);
        Assert.False(source.Negated); Assert.Equal(1500, source.CurrentAtk);
    }

    [Fact]
    public void CamelliaSentToGraveSummonsToOpponentThenSendsTheSelectedMonsterToGrave()
    {
        var send = new ProgramAbility("26077389", 99, 1, new[] { DuelZone.Hand }, c => true,
            (c, link) => c.Move(c.State.Cards.Single(card => card.DefinitionId == "63013339"), DuelZone.Graveyard));
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389" }, new[] { "89631139" } },
            ExtraDecks = new[] { new[] { "63013339" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(DuelAbilityRegistry.CreateDefault().Handlers.Concat(new[] { send })));
        var caster = duel.State.Cards.Single(c => c.DefinitionId == "26077389");
        caster.Zone = DuelZone.Hand; duel.State.Players[0].Deck.Clear();
        var source = duel.State.Cards.Single(c => c.DefinitionId == "63013339");
        source.Zone = DuelZone.ExtraMonster; source.Position = CardPosition.FaceUpAttack; source.ProperlySummoned = true;
        source.SummonedTurn = 1; duel.State.Players[0].ExtraDeck.Clear();
        var target = duel.State.Cards.Single(c => c.Owner == 1);
        target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack; duel.State.Players[1].Deck.Clear();
        duel.State.Turn = 2; duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Activate(duel, "26077389", "26077389.99"); ResolveToDecision(duel);
        AnswerFirst(duel); AnswerFirst(duel); ResolveToDecision(duel); AnswerFirst(duel);
        Assert.Equal(DuelZone.Monster, source.Zone);
        Assert.Equal(1, source.Controller);
        Assert.Equal(DuelZone.Graveyard, target.Zone);
        Assert.Contains(duel.State.Effects, record => record.Kind == EffectRecordKind.ReturnControl
            && record.Target.Equals(source.Ref) && record.Player == 0 && record.ExpiresTurn == 2);
    }

    [Fact]
    public void CamelliaSendsAStrikerCardFromDeckWhenGraveyardHasAtMostThreeSpells()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "63166096" }, Array.Empty<string>() },
            ExtraDecks = new[] { new[] { "63013339" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var source = duel.State.Cards.Single(c => c.DefinitionId == "63013339");
        source.Zone = DuelZone.ExtraMonster; source.Position = CardPosition.FaceUpAttack; duel.State.Players[0].ExtraDeck.Clear();
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Activate(duel, "63013339", "63013339.1"); ResolveToDecision(duel); AnswerFirst(duel);
        Assert.Contains(duel.State.Cards, c => c.DefinitionId == "63166096" && c.Zone == DuelZone.Graveyard);
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.AbilityId == "63013339.1");
    }

    [Fact]
    public void EngageZeroNegatesASummonTargetWithAtLeast2500Attack()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389", "37351133" }, new[] { "89631139" } },
            ExtraDecks = new[] { new[] { "17217034" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        foreach (var card in duel.State.Cards.Where(c => c.Zone == DuelZone.Deck))
        { duel.State.Players[card.Owner].Deck.Clear(); card.Zone = DuelZone.Monster; card.Slot = card.DefinitionId == "37351133" ? 1 : 0; card.Position = CardPosition.FaceUpAttack; }
        var source = duel.State.Cards.Single(c => c.DefinitionId == "17217034");
        var target = duel.State.Cards.Single(c => c.Owner == 1);
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = source.InstanceId, Cards = duel.State.Cards.Where(c => c.Owner == 0 && c.Zone == DuelZone.Monster).Select(c => c.InstanceId).ToArray(), Slot = 5 }).Accepted);
        AnswerFirst(duel); AnswerFirst(duel); ResolveToDecision(duel);
        Assert.True(target.Negated);
    }

    [Fact]
    public void EngageZeroDestroysOpponentMonstersAtDamageStartWhenBothRayeAndRozeAreInGraveyard()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389", "37351133" }, new[] { "89631139", "26077389" } },
            ExtraDecks = new[] { new[] { "17217034" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        int slot = 0;
        foreach (var card in duel.State.Cards.Where(c => c.Zone == DuelZone.Deck))
        { duel.State.Players[card.Owner].Deck.Clear(); card.Zone = card.Owner == 0 ? DuelZone.Graveyard : DuelZone.Monster;
            card.Slot = slot++; card.Position = CardPosition.FaceUpAttack; }
        var source = duel.State.Cards.Single(c => c.DefinitionId == "17217034");
        source.Zone = DuelZone.ExtraMonster; source.Position = CardPosition.FaceUpAttack; duel.State.Players[0].ExtraDeck.Clear();
        var target = duel.State.Cards.Single(c => c.DefinitionId == "89631139");
        duel.State.Turn = 2; duel.State.Phase = DuelPhase.Battle; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0,
            CardId = source.InstanceId, TargetId = target.InstanceId }).Accepted);
        for (int i = 0; i < 20 && duel.State.PendingDecision == null && duel.State.BattleStep != BattleStep.None; i++) Pass(duel);
        Assert.Equal(BattleStep.DamageStart, duel.State.BattleStep);
        AnswerFirst(duel); ResolveToDecision(duel);
        Assert.All(duel.State.Cards.Where(c => c.Owner == 1), c => Assert.Equal(DuelZone.Graveyard, c.Zone));
        Assert.Equal(8000, duel.State.Players[0].LifePoints);
    }

    [Fact]
    public void AzaleaBanishesAGraveyardSpellToDestroyItsOpponentAtDamageStepStart()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "70368879" }, new[] { "89631139" } },
            ExtraDecks = new[] { new[] { "98462037" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var spell = duel.State.Cards.Single(c => c.DefinitionId == "70368879"); spell.Zone = DuelZone.Graveyard;
        duel.State.Players[0].Deck.Clear();
        var source = duel.State.Cards.Single(c => c.DefinitionId == "98462037");
        source.Zone = DuelZone.ExtraMonster; source.Position = CardPosition.FaceUpAttack; duel.State.Players[0].ExtraDeck.Clear();
        var target = duel.State.Cards.Single(c => c.Owner == 1);
        target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack; duel.State.Players[1].Deck.Clear();
        duel.State.Turn = 2; duel.State.Phase = DuelPhase.Battle; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0,
            CardId = source.InstanceId, TargetId = target.InstanceId }).Accepted);
        for (int i = 0; i < 20 && duel.State.PendingDecision == null && duel.State.BattleStep != BattleStep.None; i++) Pass(duel);
        Assert.Equal(BattleStep.DamageStart, duel.State.BattleStep);
        AnswerFirst(duel); AnswerFirst(duel); ResolveToDecision(duel);
        Assert.Equal(DuelZone.Banished, spell.Zone);
        Assert.Equal(DuelZone.Graveyard, target.Zone);
        Assert.Equal(8000, duel.State.Players[0].LifePoints);
    }

    [Fact]
    public void AzaleaDestroysItsSummonTargetThenSendsItselfWhenGraveyardHasAtMostThreeSpells()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389", "37351133" }, new[] { "26077389" } },
            ExtraDecks = new[] { new[] { "98462037" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        foreach (var card in duel.State.Cards.Where(c => c.Zone == DuelZone.Deck))
        { duel.State.Players[card.Owner].Deck.Clear(); card.Zone = DuelZone.Monster; card.Slot = card.DefinitionId == "37351133" ? 1 : 0; card.Position = CardPosition.FaceUpAttack; }
        var source = duel.State.Cards.Single(c => c.DefinitionId == "98462037");
        var target = duel.State.Cards.Single(c => c.Owner == 1);
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0, CardId = source.InstanceId,
            Cards = duel.State.Cards.Where(c => c.Owner == 0 && c.Zone == DuelZone.Monster).Select(c => c.InstanceId).ToArray(), Slot = 5 }).Accepted);
        AnswerFirst(duel);
        var choice = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = choice.Player,
            DecisionId = choice.Id, Options = new[] { choice.Options.Single(o => o.Card.Equals(target.Ref)).Id } }).Accepted);
        ResolveToDecision(duel);
        Assert.Equal(DuelZone.Graveyard, target.Zone);
        Assert.Equal(DuelZone.Graveyard, source.Zone);
    }

    [Fact]
    public void LinkageSendsYourExistingExtraZoneCardThenSummonsAnotherStrikerWithLightDarkBonus()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "09726840", "26077389", "37351133" }, Array.Empty<string>() },
            ExtraDecks = new[] { new[] { "08491308", "12421694" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        foreach (var card in duel.State.Cards.Where(c => c.Zone == DuelZone.Deck))
        { card.Zone = card.DefinitionId == "09726840" ? DuelZone.Hand : DuelZone.Graveyard; }
        duel.State.Players[0].Deck.Clear();
        var previous = duel.State.Cards.Single(c => c.DefinitionId == "08491308");
        previous.Zone = DuelZone.ExtraMonster; previous.Position = CardPosition.FaceUpAttack;
        duel.State.Players[0].ExtraDeck.Remove(previous.InstanceId);
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Activate(duel, "09726840", "09726840.1"); ResolveToDecision(duel); AnswerFirst(duel); AnswerFirst(duel); AnswerFirst(duel);
        var target = duel.State.Cards.Single(c => c.DefinitionId == "12421694");
        Assert.Equal(DuelZone.Graveyard, previous.Zone);
        Assert.Equal(DuelZone.ExtraMonster, target.Zone);
        Assert.Equal(2500, target.CurrentAtk);
        Assert.False(target.ProperlySummoned);
    }

    [Fact]
    public void ZekeLinkSummonTemporarilyBanishesItsTargetUntilNextOpponentEnd()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389", "37351133", "70368879", "70368879" }, new[] { "26077389", "70368879", "70368879" } },
            ExtraDecks = new[] { new[] { "75147529" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var materials = duel.State.Cards.Where(c => c.Owner == 0 && (c.DefinitionId == "26077389" || c.DefinitionId == "37351133")).ToArray();
        foreach (var card in materials)
        { duel.State.Players[0].Deck.Remove(card.InstanceId); card.Zone = DuelZone.Monster; card.Slot = card.DefinitionId == "37351133" ? 1 : 0; card.Position = CardPosition.FaceUpAttack; }
        var target = duel.State.Cards.Single(c => c.Owner == 1 && c.DefinitionId == "26077389");
        duel.State.Players[1].Deck.Remove(target.InstanceId); target.Zone = DuelZone.Monster; target.Position = CardPosition.FaceUpAttack;
        var source = duel.State.Cards.Single(c => c.DefinitionId == "75147529");
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = source.InstanceId, Cards = materials.Select(c => c.InstanceId).ToArray(), Slot = 5 }).Accepted);
        AnswerFirst(duel);
        var targetChoice = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = targetChoice.Player,
            DecisionId = targetChoice.Id, Options = new[] { targetChoice.Options.Single(option => option.HasCard && option.Card.Equals(target.Ref)).Id } }).Accepted);
        ResolveToDecision(duel);
        Assert.Equal(DuelZone.Banished, target.Zone);
        int guard = 0;
        while (target.Zone == DuelZone.Banished && guard++ < 60)
        {
            if (duel.State.PendingDecision != null) AnswerFirst(duel); else Pass(duel);
        }
        Assert.Equal(DuelZone.Monster, target.Zone);
        Assert.Equal(1, target.Controller);
        Assert.Equal(2, duel.State.Turn);
    }

    [Fact]
    public void ZekeGainsAttackBeforeSendingItsOtherFieldTargetToGraveyard()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "70368879" }, Array.Empty<string>() },
            ExtraDecks = new[] { new[] { "75147529" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var target = duel.State.Cards.Single(c => c.DefinitionId == "70368879");
        target.Zone = DuelZone.SpellTrap; target.Position = CardPosition.FaceDown; duel.State.Players[0].Deck.Clear();
        var source = duel.State.Cards.Single(c => c.DefinitionId == "75147529");
        source.Zone = DuelZone.ExtraMonster; source.Position = CardPosition.FaceUpAttack; duel.State.Players[0].ExtraDeck.Clear();
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Activate(duel, "75147529", "75147529.2", target.InstanceId); ResolveToDecision(duel);
        Assert.Equal(2500, source.CurrentAtk);
        Assert.Equal(DuelZone.Graveyard, target.Zone);
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.AbilityId == "75147529.2");
    }

    [Fact]
    public void RozeRevivesAfterYourEffectRemovesAnOpponentExtraZoneMonsterAndCanNegateAnotherMonster()
    {
        var destroy = new ProgramAbility("26077389", 99, 1, new[] { DuelZone.Monster }, c => true,
            (c, link) => c.DestroyMany(link, c.State.Cards.Where(card => card.DefinitionId == "08491308")));
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389", "37351133" }, new[] { "26077389" } },
            ExtraDecks = new[] { Array.Empty<string>(), new[] { "08491308" } }, OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(DuelAbilityRegistry.CreateDefault().Handlers.Concat(new[] { destroy })));
        foreach (var card in duel.State.Cards)
        {
            duel.State.Players[card.Owner].Deck.Clear(); duel.State.Players[card.Owner].ExtraDeck.Clear();
            card.Zone = card.DefinitionId == "37351133" ? DuelZone.Graveyard
                : card.DefinitionId == "08491308" ? DuelZone.ExtraMonster : DuelZone.Monster;
            card.Position = CardPosition.FaceUpAttack;
        }
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Activate(duel, "26077389", "26077389.99"); ResolveToDecision(duel); AnswerFirst(duel);
        ResolveToDecision(duel); AnswerFirst(duel); AnswerFirst(duel);
        Assert.Contains(duel.State.Cards, c => c.DefinitionId == "37351133" && c.Zone == DuelZone.Monster);
        AnswerFirst(duel); AnswerFirst(duel);
        Assert.True(duel.State.Cards.Single(c => c.DefinitionId == "26077389" && c.Owner == 1).Negated);
    }

    [Fact]
    public void EngageDoesNotOfferBonusDrawIfCrossoutRemovesItsLastSearchCandidate()
    {
        var duel = Create("63166096", "65681983", "26077389", "73628505", "32807848", "70368879");
        foreach (var card in duel.State.Cards.Where(c => c.DefinitionId == "73628505" || c.DefinitionId == "32807848" || c.DefinitionId == "70368879"))
        { duel.State.Players[0].Deck.Remove(card.InstanceId); card.Zone = DuelZone.Graveyard; }
        var crossout = duel.State.Cards.Single(c => c.DefinitionId == "65681983");
        duel.State.Players[0].Deck.Remove(crossout.InstanceId); crossout.Zone = DuelZone.Hand;
        Activate(duel, "63166096", "63166096.1"); Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0,
            CardId = crossout.InstanceId, AbilityId = "65681983.1", NameId = "26077387", Slot = 1 }).Accepted);
        ResolveToDecision(duel); AnswerFirst(duel);
        Assert.Null(duel.State.PendingDecision);
        Assert.Empty(duel.State.Chain);
        Assert.False(duel.State.Finished);
        Assert.Contains(duel.State.Cards, c => c.DefinitionId == "26077389" && c.Zone == DuelZone.Banished);
    }

    [Fact]
    public void RozeInHandCanChainItsSummonAfterAnotherStrikerWasNormalSummoned()
    {
        var duel = Create("26077389", "37351133");
        var roze = duel.State.Cards.Single(c => c.DefinitionId == "37351133");
        roze.Zone = DuelZone.Hand; duel.State.Players[0].Deck.Remove(roze.InstanceId);
        var raye = duel.State.Cards.Single(c => c.DefinitionId == "26077389");
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 0,
            CardId = raye.InstanceId, Slot = 0 }).Accepted);
        Assert.Contains(duel.QueryLegalActions(0), a => a.AbilityId == "37351133.1");
        Activate(duel, "37351133", "37351133.1"); ResolveToDecision(duel); AnswerFirst(duel); AnswerFirst(duel);
        Assert.Equal(DuelZone.Monster, roze.Zone);
        Assert.Equal(0, roze.Controller);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CyanosGraveyardEffectBanishesItselfAndLetsPlayerRecoverOrSummonRoze(bool summon)
    {
        var duel = Create("20357457", "37351133");
        var source = duel.State.Cards.Single(c => c.DefinitionId == "20357457"); source.Zone = DuelZone.Graveyard;
        var roze = duel.State.Cards.Single(c => c.DefinitionId == "37351133");
        duel.State.Players[0].Deck.Clear(); roze.Zone = DuelZone.Banished; roze.Position = CardPosition.FaceUp;
        Activate(duel, "20357457", "20357457.3");
        Assert.Equal(DuelZone.Banished, source.Zone);
        ResolveToDecision(duel); AnswerFirst(duel);
        var mode = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0,
            DecisionId = mode.Id, Options = new[] { mode.Options.Single(o => o.Value == (summon ? "summon" : "hand")).Id } }).Accepted);
        if (summon) { AnswerFirst(duel); AnswerFirst(duel); }
        Assert.Equal(summon ? DuelZone.Monster : DuelZone.Hand, roze.Zone);
    }

    [Fact]
    public void CyanosNormalSummonTriggersRozeFromDeckAndRestrictsExtraSummonsToMachines()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "20357457", "37351133" }, Array.Empty<string>() },
            ExtraDecks = new[] { new[] { "01948619", "08491308" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var source = duel.State.Cards.Single(c => c.DefinitionId == "20357457");
        source.Zone = DuelZone.Hand; duel.State.Players[0].Deck.Remove(source.InstanceId);
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.NormalSummon, Player = 0,
            CardId = source.InstanceId, Slot = 0 }).Accepted);
        AnswerFirst(duel); ResolveToDecision(duel); AnswerFirst(duel); AnswerFirst(duel); AnswerFirst(duel);
        Assert.Contains(duel.State.Cards, c => c.DefinitionId == "37351133" && c.Zone == DuelZone.Monster);
        Assert.False(duel.CanSpecialSummonConditions(duel.State.Cards.Single(c => c.DefinitionId == "01948619"), 0, ignore: true));
        Assert.True(duel.CanSpecialSummonConditions(duel.State.Cards.Single(c => c.DefinitionId == "08491308"), 0, ignore: true));
    }

    [Theory]
    [InlineData(DuelZone.Hand, false)]
    [InlineData(DuelZone.Graveyard, true)]
    public void BlackGoatBlocksTheDeclaredOriginalNameOutsideGraveyardAndSharesBothEffectsLimit(DuelZone zone, bool allowed)
    {
        var summon = new ProgramAbility("38517737", 99, 1, new[] { DuelZone.Hand, DuelZone.Graveyard },
            c => c.CanSpecialSummon(c.Source, c.Player, ignore: true), (c, link) => { });
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "49299410", "38517737" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(DuelAbilityRegistry.CreateDefault().Handlers.Concat(new[] { summon })));
        var goat = duel.State.Cards.Single(c => c.DefinitionId == "49299410");
        goat.Zone = DuelZone.SpellTrap; goat.Position = CardPosition.FaceDown; goat.SetTurn = 0;
        var target = duel.State.Cards.Single(c => c.DefinitionId == "38517737");
        target.Zone = zone; target.ProperlySummoned = true; duel.State.Players[0].Deck.Clear();
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 0, CardId = goat.InstanceId,
            AbilityId = "49299410.1", NameId = "38517737" });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal("38517737", result.Events.Single(e => e.Kind == DuelEventKind.Activated).DeclaredNameId);
        ResolveToDecision(duel);
        Assert.Equal(allowed, duel.QueryLegalActions(0).Any(a => a.AbilityId == "38517737.99"));
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.AbilityId == "49299410.2");
    }

    [Fact]
    public void CyanosDiscardsASpellAsCostThenSpecialSummonsItselfFromHand()
    {
        var duel = Create("20357457", "73628505");
        var spell = duel.State.Cards.Single(c => c.DefinitionId == "73628505");
        duel.State.Players[0].Deck.Remove(spell.InstanceId); spell.Zone = DuelZone.Hand;
        Activate(duel, "20357457", "20357457.1", costs: new[] { spell.InstanceId });
        Assert.Equal(DuelZone.Graveyard, spell.Zone);
        ResolveToDecision(duel); AnswerFirst(duel); AnswerFirst(duel);
        Assert.Contains(duel.State.Cards, c => c.DefinitionId == "20357457" && c.Zone == DuelZone.Monster);
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.AbilityId == "20357457.1");
    }

    [Fact]
    public void WidowAnchorNegatesThenOptionallyTakesControlUntilTheEndPhase()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "98338152", "73628505", "32807848", "70368879" }, new[] { "26077389" } },
            OpeningHand = 0, Shuffle = false
        });
        foreach (var card in duel.State.Cards)
        {
            duel.State.Players[card.Owner].Deck.Clear();
            card.Zone = card.Owner == 1 ? DuelZone.Monster : card.DefinitionId == "98338152" ? DuelZone.Hand : DuelZone.Graveyard;
            card.Position = CardPosition.FaceUpAttack;
        }
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        var target = duel.State.Cards.Single(c => c.Owner == 1);
        Activate(duel, "98338152", "98338152.1", target.InstanceId);
        ResolveToDecision(duel);
        Assert.True(target.Negated);
        Assert.Equal(DecisionKind.YesNo, duel.State.PendingDecision!.Kind);
        AnswerFirst(duel); AnswerFirst(duel);
        Assert.Equal(0, target.Controller); Assert.Equal(1, target.Owner);
        ResolveToDecision(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        Pass(duel); Pass(duel); Pass(duel);
        Assert.Equal(1, target.Controller);
        Assert.False(target.Negated);
    }

    [Fact]
    public void ShizukuReducesOpponentStatsAndOnlySearchesSpellNamesAbsentFromYourGraveyardAtEnd()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "63166096", "63166096", "98338152" }, new[] { "26077389" } },
            ExtraDecks = new[] { new[] { "90673289" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var grave = duel.State.Cards.First(c => c.DefinitionId == "63166096");
        grave.Zone = DuelZone.Graveyard; duel.State.Players[0].Deck.Remove(grave.InstanceId);
        var shizuku = duel.State.Cards.Single(c => c.DefinitionId == "90673289");
        shizuku.Zone = DuelZone.ExtraMonster; shizuku.Position = CardPosition.FaceUpAttack; shizuku.SummonedTurn = 1;
        duel.State.Players[0].ExtraDeck.Clear();
        var opponent = duel.State.Cards.Single(c => c.Owner == 1);
        opponent.Zone = DuelZone.Monster; opponent.Position = CardPosition.FaceUpAttack; duel.State.Players[1].Deck.Clear();
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = 0, Phase = DuelPhase.End }).Accepted);
        Pass(duel);
        Assert.Equal(1400, opponent.CurrentAtk); Assert.Equal(1400, opponent.CurrentDef);
        AnswerFirst(duel); ResolveToDecision(duel);
        var option = Assert.Single(duel.State.PendingDecision!.Options);
        Assert.Equal("98338152", duel.State.Cards.Single(c => c.InstanceId == option.Card.InstanceId).DefinitionId);
        AnswerFirst(duel);
        Assert.Contains(duel.State.Cards, c => c.DefinitionId == "98338152" && c.Zone == DuelZone.Hand);
    }

    [Fact]
    public void KainaStopsItsSummonTargetFromAttackingUntilTheNextOpponentTurnEnds()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389", "70368879", "70368879" }, new[] { "26077389", "89631139", "89631139" } },
            ExtraDecks = new[] { new[] { "12421694" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var materials = duel.State.Cards.Where(c => c.DefinitionId == "26077389").ToArray();
        foreach (var card in materials)
        { duel.State.Players[card.Owner].Deck.Remove(card.InstanceId); card.Zone = DuelZone.Monster; card.Position = CardPosition.FaceUpAttack; }
        var source = duel.State.Cards.Single(c => c.DefinitionId == "12421694");
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = source.InstanceId, Cards = new[] { materials.Single(c => c.Owner == 0).InstanceId }, Slot = 5 }).Accepted);
        AnswerFirst(duel); AnswerFirst(duel); ResolveToDecision(duel);
        while (duel.State.Turn < 2) Pass(duel);
        EnterBattle(duel);
        Assert.DoesNotContain(duel.QueryLegalActions(1), a => a.Kind == DuelCommandKind.Attack);
        while (duel.State.Turn < 4) Pass(duel);
        EnterBattle(duel);
        Assert.Contains(duel.QueryLegalActions(1), a => a.Kind == DuelCommandKind.Attack);
    }

    [Fact]
    public void KainaRecoversLifeAfterYourStrikerSpellFinishesItsChoicesWithoutOpeningAnotherChain()
    {
        var duel = Create("63166096", "26077389", "12421694");
        var kaina = duel.State.Cards.Single(c => c.DefinitionId == "12421694");
        duel.State.Players[0].Deck.Remove(kaina.InstanceId); kaina.Zone = DuelZone.ExtraMonster; kaina.Position = CardPosition.FaceUpAttack;
        Activate(duel, "63166096", "63166096.1");
        Assert.Equal(8000, duel.State.Players[0].LifePoints);
        Assert.Single(duel.State.Chain);
        Assert.Equal("63166096.1", duel.State.Chain[0].AbilityId);
        ResolveToDecision(duel);
        Assert.Equal(8000, duel.State.Players[0].LifePoints);
        AnswerFirst(duel);
        Assert.Equal(8100, duel.State.Players[0].LifePoints);
        Assert.Empty(duel.State.Chain);
    }

    [Fact]
    public void KainaDoesNotRecoverWhenMultiroleSendsItToGraveyardDuringResolution()
    {
        var duel = Create("24010609", "12421694");
        var kaina = duel.State.Cards.Single(c => c.DefinitionId == "12421694");
        duel.State.Players[0].Deck.Remove(kaina.InstanceId);
        kaina.Zone = DuelZone.ExtraMonster; kaina.Position = CardPosition.FaceUpAttack;
        var multirole = duel.State.Cards.Single(c => c.DefinitionId == "24010609");
        multirole.Zone = DuelZone.SpellTrap; multirole.Position = CardPosition.FaceUp;
        Activate(duel, "24010609", "24010609.1", kaina.InstanceId);
        ResolveToDecision(duel);
        Assert.Equal(DuelZone.Graveyard, kaina.Zone);
        Assert.Equal(8000, duel.State.Players[0].LifePoints);
    }

    [Theory]
    [InlineData(false, 8100)]
    [InlineData(true, 8000)]
    public void KainaRecoversAfterEffectNegationButNotActivationNegation(bool negateActivation, int expectedLife)
    {
        // 专用响应处理器隔离公共无效与回复时点，闪刀效果和魁奈仍使用真实规则。
        var negate = new ProgramAbility("97268402", 99, 2, new[] { DuelZone.Hand }, c => true,
            (c, link) => { var previous = c.State.Chain[link.Number - 2];
                if (negateActivation) previous.ActivationNegated = true; else previous.EffectNegated = true; });
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "63166096", "26077389", "12421694" }, new[] { "97268402" } },
            OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(DuelAbilityRegistry.CreateDefault().Handlers.Concat(new[] { negate })));
        foreach (var card in duel.State.Cards.Where(c => c.DefinitionId != "26077389"))
        { duel.State.Players[card.Owner].Deck.Remove(card.InstanceId);
            card.Zone = card.DefinitionId == "12421694" ? DuelZone.ExtraMonster : DuelZone.Hand;
            card.Position = CardPosition.FaceUpAttack; }
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Activate(duel, "63166096", "63166096.1");
        Activate(duel, "97268402", "97268402.99");
        ResolveToDecision(duel);
        Assert.Equal(expectedLife, duel.State.Players[0].LifePoints);
        Assert.Null(duel.State.PendingDecision);
    }

    [Theory]
    [InlineData("08491308")]
    [InlineData("12421694")]
    [InlineData("63288574")]
    [InlineData("90673289")]
    public void EachLinkOneStrikerCanOnlyBeSpecialSummonedOncePerTurn(string cardId)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389" }, Array.Empty<string>() },
            ExtraDecks = new[] { new[] { cardId }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var material = duel.State.Cards.Single(c => c.DefinitionId == "26077389");
        material.Zone = DuelZone.Monster; material.Position = CardPosition.FaceUpAttack;
        duel.State.Players[0].Deck.Clear();
        duel.State.TurnFacts.Add(new DuelEvent { Kind = DuelEventKind.Summoned, Player = 0,
            DefinitionId = cardId, SummonMethod = SummonMethod.Link });
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        var source = duel.State.Cards.Single(c => c.DefinitionId == cardId);
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.Kind == DuelCommandKind.SpecialSummon && a.Card.Equals(source.Ref));
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = source.InstanceId, Cards = new[] { material.InstanceId }, Slot = 5 }).Accepted);
    }

    [Fact]
    public void KagariTargetsAGraveyardStrikerSpellAndItsAttackBonusUpdatesAfterRecovery()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389", "63166096" }, Array.Empty<string>() },
            ExtraDecks = new[] { new[] { "63288574" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var material = duel.State.Cards.Single(c => c.DefinitionId == "26077389");
        var spell = duel.State.Cards.Single(c => c.DefinitionId == "63166096");
        var target = duel.State.Cards.Single(c => c.DefinitionId == "63288574");
        material.Zone = DuelZone.Monster; material.Position = CardPosition.FaceUpAttack;
        spell.Zone = DuelZone.Graveyard; duel.State.Players[0].Deck.Clear();
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.SpecialSummon, Player = 0,
            CardId = target.InstanceId, Cards = new[] { material.InstanceId }, Slot = 5 });
        Assert.True(result.Accepted, result.Error);
        Assert.Equal(1600, target.CurrentAtk);
        AnswerFirst(duel); // Accept the optional summon trigger.
        var choice = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal("trigger.target", choice.Continuation);
        Assert.Equal(spell.Ref, Assert.Single(choice.Options).Card);
        AnswerFirst(duel); ResolveToDecision(duel);
        Assert.Equal(DuelZone.Hand, spell.Zone);
        Assert.Equal(1500, target.CurrentAtk);
    }

    [Fact]
    public void HayateOffersItsDeckSendOnlyAfterBattleDamageCalculation()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "63166096" }, new[] { "89631139" } },
            ExtraDecks = new[] { new[] { "08491308" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var attacker = duel.State.Cards.Single(c => c.DefinitionId == "08491308");
        attacker.Zone = DuelZone.ExtraMonster; attacker.Position = CardPosition.FaceUpAttack;
        duel.State.Players[0].ExtraDeck.Clear();
        var defender = duel.State.Cards.Single(c => c.Owner == 1);
        defender.Zone = DuelZone.Monster; defender.Position = CardPosition.FaceUpAttack;
        duel.State.Players[1].Deck.Clear();
        duel.State.Turn = 2; duel.State.Phase = DuelPhase.Battle; duel.State.Window = TimingWindow.Open;
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0, CardId = attacker.InstanceId }).Accepted);
        for (int i = 0; i < 20 && duel.State.PendingDecision == null && duel.State.BattleStep != BattleStep.None; i++)
            Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
        var offer = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal(BattleStep.AfterCalculation, duel.State.BattleStep);
        Assert.Equal(6500, duel.State.Players[1].LifePoints);
        AnswerFirst(duel); ResolveToDecision(duel); AnswerFirst(duel);
        Assert.Contains(duel.State.Cards, c => c.DefinitionId == "63166096" && c.Zone == DuelZone.Graveyard);
    }

    [Fact]
    public void HayateCanAttackDirectlyWhileOpponentControlsAMonster()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { Array.Empty<string>(), new[] { "89631139" } },
            ExtraDecks = new[] { new[] { "08491308" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        foreach (var card in duel.State.Cards)
        {
            duel.State.Players[card.Owner].Deck.Clear(); duel.State.Players[card.Owner].ExtraDeck.Clear();
            card.Zone = card.Owner == 0 ? DuelZone.ExtraMonster : DuelZone.Monster;
            card.Position = CardPosition.FaceUpAttack;
        }
        duel.State.Turn = 2; duel.State.Phase = DuelPhase.Battle; duel.State.Window = TimingWindow.Open;
        var attacker = duel.State.Cards.Single(c => c.Owner == 0);
        Assert.Contains(duel.QueryLegalActions(0), a => a.Kind == DuelCommandKind.Attack && a.CanAttackDirectly);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0, CardId = attacker.InstanceId });
        Assert.True(result.Accepted, result.Error);
    }

    [Fact]
    public void RayeInGraveyardRespondsWhenOpponentEffectRemovesYourStrikerLink()
    {
        var destroy = new ProgramAbility("40044918", 99, 1, new[] { DuelZone.Monster }, c => true,
            (c, link) => c.Destroy(c.State.Cards.Single(card => card.DefinitionId == "08491308")));
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389" }, new[] { "40044918" } },
            ExtraDecks = new[] { new[] { "08491308" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(DuelAbilityRegistry.CreateDefault().Handlers.Concat(new[] { destroy })));
        foreach (var card in duel.State.Cards)
        {
            duel.State.Players[card.Owner].Deck.Clear(); duel.State.Players[card.Owner].ExtraDeck.Clear();
            card.Zone = card.DefinitionId == "26077389" ? DuelZone.Graveyard
                : card.DefinitionId == "08491308" ? DuelZone.ExtraMonster : DuelZone.Monster;
            card.Position = CardPosition.FaceUpAttack;
        }
        duel.State.TurnPlayer = 1; duel.State.WaitingSeat = 1;
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Activate(duel, "40044918", "40044918.99"); ResolveToDecision(duel);
        var offer = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal(0, offer.Player); Assert.Equal("trigger.offer", offer.Continuation);
        AnswerFirst(duel); ResolveToDecision(duel); AnswerFirst(duel); AnswerFirst(duel);
        Assert.Contains(duel.State.Cards, c => c.DefinitionId == "26077389" && c.Zone == DuelZone.Monster && c.Controller == 0);
    }

    [Fact]
    public void RayePaysItsTributeOnceThenChoosesAnExtraZoneStrikerWithoutAProperLinkSummon()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "26077389" }, Array.Empty<string>() },
            ExtraDecks = new[] { new[] { "08491308" }, Array.Empty<string>() }, OpeningHand = 0, Shuffle = false
        });
        var source = duel.State.Cards.Single(c => c.DefinitionId == "26077389");
        duel.State.Players[0].Deck.Clear(); source.Zone = DuelZone.Monster; source.Position = CardPosition.FaceUpAttack;
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        Activate(duel, "26077389", "26077389.1");
        Assert.Equal(DuelZone.Graveyard, source.Zone);
        ResolveToDecision(duel); AnswerFirst(duel); AnswerFirst(duel);
        var target = duel.State.Cards.Single(c => c.DefinitionId == "08491308");
        Assert.Equal(DuelZone.ExtraMonster, target.Zone);
        Assert.False(target.ProperlySummoned);
        Assert.Equal(SummonMethod.Effect, target.SummonMethod);
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.AbilityId == "26077389.1");
    }

    [Fact]
    public void EngageSearchesAnotherStrikerCardThenOptionallyDrawsWithThreeGraveyardSpells()
    {
        var duel = Create("63166096", "26077389", "73628505", "32807848", "70368879", "24094653");
        foreach (string id in new[] { "73628505", "32807848", "70368879" })
        {
            var card = duel.State.Cards.Single(c => c.DefinitionId == id);
            duel.State.Players[0].Deck.Remove(card.InstanceId); card.Zone = DuelZone.Graveyard;
        }
        Activate(duel, "63166096", "63166096.1"); ResolveToDecision(duel); AnswerFirst(duel);
        var draw = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal(DecisionKind.YesNo, draw.Kind);
        AnswerFirst(duel);
        Assert.Contains(duel.State.Cards, c => c.DefinitionId == "26077389" && c.Zone == DuelZone.Hand);
        Assert.Contains(duel.State.Cards, c => c.DefinitionId == "24094653" && c.Zone == DuelZone.Hand);
        Assert.Empty(duel.State.Chain);
    }

    [Fact]
    public void TerraformingSearchesAFieldSpellDuringResolution()
    {
        var duel = Create("73628505", "50005218");
        Activate(duel, "73628505", "73628505.1");
        ResolveToDecision(duel);
        var choice = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Single(choice.Options);
        AnswerFirst(duel);
        Assert.Contains(duel.State.Cards, c => c.DefinitionId == "50005218" && c.Zone == DuelZone.Hand);
        Assert.Empty(duel.State.Chain);
    }

    static DuelEngine Create(string hand, params string[] deck)
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { hand }.Concat(deck).ToArray(), Array.Empty<string>() },
            OpeningHand = 0, Shuffle = false
        });
        var card = duel.State.Cards.First(c => c.DefinitionId == hand);
        duel.State.Players[0].Deck.Remove(card.InstanceId); card.Zone = DuelZone.Hand;
        duel.State.Phase = DuelPhase.Main1; duel.State.Window = TimingWindow.Open;
        return duel;
    }

    static void Activate(DuelEngine duel, string cardId, string abilityId, int targetId = 0, int[]? costs = null)
    {
        var source = duel.State.Cards.First(c => c.DefinitionId == cardId && c.Controller == duel.State.WaitingSeat);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = duel.State.WaitingSeat,
            CardId = source.InstanceId, AbilityId = abilityId, TargetId = targetId, Cards = costs ?? Array.Empty<int>() });
        Assert.True(result.Accepted, result.Error);
    }

    static void ResolveToDecision(DuelEngine duel)
    {
        for (int i = 0; i < 30 && duel.State.PendingDecision == null && duel.State.Window != TimingWindow.Open; i++)
        {
            var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat });
            Assert.True(result.Accepted, result.Error);
        }
    }

    static void AnswerFirst(DuelEngine duel)
    {
        var choice = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = choice.Player,
            DecisionId = choice.Id, Options = new[] { choice.Options[0].Id } });
        Assert.True(result.Accepted, result.Error);
    }

    static void Pass(DuelEngine duel)
    {
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat });
        Assert.True(result.Accepted, result.Error);
    }

    static void EnterBattle(DuelEngine duel)
    {
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.AdvancePhase, Player = duel.State.TurnPlayer,
            Phase = DuelPhase.Battle });
        Assert.True(result.Accepted, result.Error); Pass(duel);
    }
}
