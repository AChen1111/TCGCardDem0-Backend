using System.Text.Json;
using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class ProjectionTests
{
    [Fact]
    public void A_name_declaration_action_exposes_its_kind_and_transfers_the_players_public_declaration()
    {
        var state = new DuelState();
        var projection = new SeatProjection(0);
        var offered = Assert.Single(projection.Project(state, new[] { new DuelAction { Id = "declare", Kind = DuelCommandKind.Activate,
            AbilityId = "declaration", RequiresNameDeclaration = true, DeclarationKind = RuleCardKind.Monster } }).Actions);
        Assert.True(offered.RequiresNameDeclaration);
        Assert.Equal(RuleCardKind.Monster, offered.DeclarationKind);
        Assert.True(projection.TryResolveInput(state, new SeatInput { ActionToken = offered.ActionToken, NameId = "89631139" }, out var command));
        Assert.Equal("89631139", command.NameId);
    }

    [Fact]
    public void An_activation_action_keeps_its_selected_mode_instead_of_accepting_a_forged_mode()
    {
        var state = new DuelState();
        var projection = new SeatProjection(0);
        var offered = Assert.Single(projection.Project(state, new[] { new DuelAction { Id = "activate.mode", Kind = DuelCommandKind.Activate,
            AbilityId = "multi-mode", Label = "抽牌", ActivationOptions = new[] { "draw" } } }).Actions);
        Assert.Equal("抽牌", offered.Label);
        Assert.True(projection.TryResolveInput(state, new SeatInput { ActionToken = offered.ActionToken,
            OptionTokens = new[] { "steal" } }, out var command));
        Assert.Equal(new[] { "draw" }, command.Options);
    }

    [Fact]
    public void A_summon_action_cannot_be_repurposed_to_an_unoffered_face_down_position()
    {
        var state = new DuelState { Revision = 4 };
        var card = Card(55, "normal-monster", 0, DuelZone.Hand);
        state.Cards.Add(card);
        var projection = new SeatProjection(0);
        var action = Assert.Single(projection.Project(state, new[] { new DuelAction { Id = "summon", Kind = DuelCommandKind.NormalSummon,
            Card = card.Ref, Slots = new List<int> { 0 }, Positions = new List<CardPosition> { CardPosition.FaceUpAttack } } }).Actions);
        Assert.Equal(new[] { CardPosition.FaceUpAttack }, action.Positions);
        Assert.False(projection.TryResolveInput(state, new SeatInput { Revision = 4, ActionToken = action.ActionToken,
            Position = CardPosition.FaceDownDefense }, out _));
        Assert.True(projection.TryResolveInput(state, new SeatInput { Revision = 4, ActionToken = action.ActionToken,
            Position = CardPosition.FaceUpAttack }, out var command));
        Assert.Equal(CardPosition.FaceUpAttack, command.Position);
    }

    [Fact]
    public void A_projected_direct_attack_can_omit_an_opponent_target_only_when_the_rule_offers_it()
    {
        var state = new DuelState { Revision = 4 };
        var attacker = Card(55, "attacker", 0, DuelZone.Monster, CardPosition.FaceUpAttack);
        var target = Card(91, "target", 1, DuelZone.Monster, CardPosition.FaceUpDefense);
        state.Cards.AddRange(new[] { attacker, target });
        var legal = new DuelAction { Id = "attack", Kind = DuelCommandKind.Attack, Card = attacker.Ref,
            Targets = new List<CardRef> { target.Ref }, CanAttackDirectly = true };
        var projection = new SeatProjection(0);
        var offered = Assert.Single(projection.Project(state, new[] { legal }).Actions);
        Assert.True(offered.CanAttackDirectly);
        Assert.True(projection.TryResolveInput(state, new SeatInput { Revision = 4, ActionToken = offered.ActionToken }, out var command));
        Assert.Equal(0, command.TargetId);
        legal.CanAttackDirectly = false;
        offered = Assert.Single(projection.Project(state, new[] { legal }).Actions);
        Assert.False(projection.TryResolveInput(state, new SeatInput { Revision = 4, ActionToken = offered.ActionToken }, out _));
    }

    [Fact]
    public void An_opponent_decision_does_not_block_the_players_own_surrender_action()
    {
        var state = new DuelState { Revision = 3, Window = TimingWindow.Decision, WaitingSeat = 1,
            PendingDecision = new DuelDecision { Id = 8, Player = 1, Min = 1, Max = 1 } };
        var projection = new SeatProjection(0);
        var view = projection.Project(state, new[] { new DuelAction { Id = "surrender", Kind = DuelCommandKind.Surrender } });
        Assert.Null(view.Decision);
        Assert.True(projection.TryResolveInput(state, new SeatInput { Revision = 3,
            ActionToken = Assert.Single(view.Actions).ActionToken }, out var command));
        Assert.Equal(DuelCommandKind.Surrender, command.Kind);
    }

    [Fact]
    public void SeatSeesOwnHandButReceivesOnlyCountsForOpponentHandAndBothDecks()
    {
        var state = new DuelState { RandomState = 987654321 };
        state.Cards.Add(Card(1, "own-hand", 0, DuelZone.Hand));
        state.Cards.Add(Card(2, "opponent-secret", 1, DuelZone.Hand));
        state.Cards.Add(Card(3, "own-deck-secret", 0, DuelZone.Deck));
        state.Cards.Add(Card(4, "opponent-deck-secret", 1, DuelZone.Deck));
        var view = new SeatProjection(0).Project(state);

        Assert.Equal("own-hand", Assert.Single(view.Cards).DefinitionId);
        Assert.Equal(1, view.Players[1].HandCount);
        Assert.Equal(1, view.Players[0].DeckCount);
        Assert.Equal(1, view.Players[1].DeckCount);
        var wire = JsonSerializer.Serialize(view);
        Assert.DoesNotContain("opponent-secret", wire);
        Assert.DoesNotContain("own-deck-secret", wire);
        Assert.DoesNotContain("opponent-deck-secret", wire);
        Assert.DoesNotContain("RandomState", wire);
        Assert.DoesNotContain("InstanceId", wire);
        Assert.DoesNotContain("987654321", wire);
    }

    [Fact]
    public void FaceDownOpponentCardKeepsItsPublicPositionWithoutLeakingItsIdentityOrStats()
    {
        var state = new DuelState();
        state.Cards.Add(Card(1, "set-secret", 1, DuelZone.Monster, CardPosition.FaceDownDefense));
        state.Cards.Add(Card(2, "public-monster", 1, DuelZone.Monster, CardPosition.FaceUpAttack));
        state.Cards.Add(Card(3, "own-extra", 0, DuelZone.ExtraDeck));
        state.Cards.Add(Card(4, "secret-extra", 1, DuelZone.ExtraDeck));
        var projection = new SeatProjection(0);
        var view = projection.Project(state);

        Assert.Equal(3, view.Cards.Count);
        var unknown = view.Cards.Single(c => c.Position == CardPosition.FaceDownDefense);
        Assert.Equal(string.Empty, unknown.DefinitionId);
        Assert.Null(unknown.Attack);
        Assert.Null(unknown.Defense);
        Assert.NotEqual(string.Empty, unknown.ViewCardId);
        Assert.Equal(unknown.ViewCardId, projection.Project(state).Cards.Single(c => c.Position == CardPosition.FaceDownDefense).ViewCardId);
        Assert.NotEqual(unknown.ViewCardId, new SeatProjection(1).Project(state).Cards.Single(c => c.DefinitionId == "set-secret").ViewCardId);
        Assert.DoesNotContain("set-secret", JsonSerializer.Serialize(view));
        Assert.DoesNotContain("secret-extra", JsonSerializer.Serialize(view));
    }

    [Fact]
    public void ConcealmentAndTrackingEpochEndTheOldDisplayIdentity()
    {
        var state = new DuelState();
        var card = Card(1, "known", 0, DuelZone.Hand);
        state.Cards.Add(card);
        var projector = new SeatProjection(0);
        string original = Assert.Single(projector.Project(state).Cards).ViewCardId;
        card.Zone = DuelZone.Deck;
        Assert.Empty(projector.Project(state).Cards);
        card.Zone = DuelZone.Hand;
        string returned = Assert.Single(projector.Project(state).Cards).ViewCardId;
        Assert.NotEqual(original, returned);
        card.TrackingEpoch++;
        Assert.NotEqual(returned, Assert.Single(projector.Project(state).Cards).ViewCardId);
    }

    [Fact]
    public void OnlyTheAnsweringSeatGetsOpaqueDecisionOptionsAndStaleAnswersCannotResolve()
    {
        var state = new DuelState { Revision = 7 };
        var card = Card(41, "search-card", 0, DuelZone.Deck);
        state.Cards.Add(card);
        state.PendingDecision = new DuelDecision
        {
            Id = 19, Player = 0, Kind = DecisionKind.ChooseCards, Min = 1, Max = 1,
            Options = new List<DecisionOption> { new DecisionOption { Id = "internal-41", Card = card.Ref, HasCard = true, Label = "search-card" } }
        };
        var own = new SeatProjection(0);
        var opponent = new SeatProjection(1);
        var view = own.Project(state);
        var option = Assert.Single(view.Decision.Options);
        Assert.NotEqual("internal-41", option.OptionToken);
        Assert.DoesNotContain("internal-41", JsonSerializer.Serialize(view));
        Assert.Null(opponent.Project(state).Decision);
        var input = new SeatInput { Revision = 7, OptionTokens = new[] { option.OptionToken } };
        Assert.True(own.TryResolveInput(state, input, out var command));
        Assert.Equal(0, command.Player);
        Assert.Equal(19, command.DecisionId);
        Assert.Equal(new[] { "internal-41" }, command.Options);
        Assert.False(opponent.TryResolveInput(state, input, out _));
        state.Revision++;
        Assert.False(own.TryResolveInput(state, input, out _));
    }

    [Fact]
    public void BlindHandChoiceRedactsTheCandidateNameEvenWhenHandCardsUseFaceUpPosition()
    {
        var state = new DuelState();
        var card = Card(77, "opponents-hand-secret", 1, DuelZone.Hand, CardPosition.FaceUp);
        state.Cards.Add(card);
        state.PendingDecision = new DuelDecision
        {
            Id = 5, Player = 0, Kind = DecisionKind.ChooseCards, Min = 1, Max = 1,
            Options = new List<DecisionOption> { new DecisionOption { Id = "private77", Card = card.Ref,
                HasCard = true, Label = "opponents-hand-secret" } }
        };
        var view = new SeatProjection(0).Project(state);
        Assert.Empty(view.Cards);
        Assert.Equal("", Assert.Single(view.Decision.Options).DefinitionId);
        Assert.DoesNotContain("opponents-hand-secret", JsonSerializer.Serialize(view));
    }

    [Fact]
    public void ActionTokensResolveOnlyOfferedSourceTargetsAndCurrentCardExistence()
    {
        var state = new DuelState { Revision = 4 };
        var attacker = Card(55, "attacker", 0, DuelZone.Monster, CardPosition.FaceUpAttack);
        var target = Card(91, "target", 1, DuelZone.Monster, CardPosition.FaceUpDefense);
        state.Cards.AddRange(new[] { attacker, target });
        var legal = new[] { new DuelAction { Id = "internal-attack-55", Kind = DuelCommandKind.Attack,
            Card = attacker.Ref, Targets = new List<CardRef> { target.Ref } } };
        var projector = new SeatProjection(0);
        var view = projector.Project(state, legal);
        var action = Assert.Single(view.Actions);
        Assert.DoesNotContain("internal-attack-55", JsonSerializer.Serialize(view));
        var input = new SeatInput { Revision = 4, ActionToken = action.ActionToken,
            TargetViewCardId = Assert.Single(action.TargetViewCardIds) };
        Assert.True(projector.TryResolveInput(state, input, out var command));
        Assert.Equal(55, command.CardId);
        Assert.Equal(91, command.TargetId);
        Assert.Equal(0, command.Player);
        input.TargetViewCardId = action.SourceViewCardId;
        Assert.False(projector.TryResolveInput(state, input, out _));
        input.TargetViewCardId = action.TargetViewCardIds[0];
        attacker.Generation++;
        Assert.False(projector.TryResolveInput(state, input, out _));
    }

    [Fact]
    public void RepeatedSnapshotAtTheSameRevisionDoesNotRevokeAnOfferedAction()
    {
        var state = new DuelState { Revision = 4 };
        var actions = new[] { new DuelAction { Id = "pass", Kind = DuelCommandKind.Pass } };
        var projector = new SeatProjection(0);
        string token = Assert.Single(projector.Project(state, actions).Actions).ActionToken;
        Assert.Equal(token, Assert.Single(projector.Project(state, actions).Actions).ActionToken);
        Assert.True(projector.TryResolveInput(state, new SeatInput { Revision = 4, ActionToken = token }, out _));
    }

    [Fact]
    public void PublicTurnAndChainSnapshotDoesNotPublishPrivateExecutionDataOrMutateAfterProjection()
    {
        var state = new DuelState { Turn = 3, TurnPlayer = 1, Phase = DuelPhase.Battle,
            Window = TimingWindow.ChainResponse, WaitingSeat = 0 };
        state.Chain.Add(new DuelChainLink { Number = 1, Player = 1, DefinitionId = "activated-card",
            AbilityId = "draw", Program = "private-program", Values = new Dictionary<string, int> { ["hidden-id"] = 44 },
            Costs = new List<CardRef> { new CardRef(12, 8) } });
        var view = new SeatProjection(0).Project(state);
        Assert.Equal(3, view.Turn);
        Assert.Equal(1, view.TurnPlayer);
        Assert.Equal(DuelPhase.Battle, view.Phase);
        Assert.Equal(TimingWindow.ChainResponse, view.Window);
        Assert.Equal(0, view.WaitingSeat);
        Assert.Equal("activated-card", Assert.Single(view.Chain).DefinitionId);
        state.Players[0].LifePoints = 10;
        state.Chain[0].DefinitionId = "changed";
        Assert.Equal(8000, view.Players[0].LifePoints);
        Assert.Equal("activated-card", view.Chain[0].DefinitionId);
        Assert.DoesNotContain("private-program", JsonSerializer.Serialize(view));
        Assert.DoesNotContain("Costs", JsonSerializer.Serialize(view));
        Assert.DoesNotContain("hidden-id", JsonSerializer.Serialize(view));
    }

    [Fact]
    public void PhaseActionTokenCannotBeRepurposedForAnotherTransition()
    {
        var state = new DuelState();
        var projector = new SeatProjection(0);
        var view = projector.Project(state, new[] { new DuelAction { Id = "end", Kind = DuelCommandKind.AdvancePhase, Phase = DuelPhase.End } });
        var action = Assert.Single(view.Actions);
        Assert.Equal(DuelPhase.End, action.Phase);
        Assert.True(projector.TryResolveInput(state, new SeatInput { ActionToken = action.ActionToken, Phase = DuelPhase.Battle }, out var command));
        Assert.Equal(DuelPhase.End, command.Phase);
    }

    [Fact]
    public void MaterialSelectionAcceptsOnlyDistinctOfferedVisibleCardsWithinTheActionBounds()
    {
        var state = new DuelState();
        var source = Card(10, "high-level", 0, DuelZone.Hand);
        var tribute = Card(20, "tribute", 0, DuelZone.Monster, CardPosition.FaceUpAttack);
        var other = Card(30, "opponent", 1, DuelZone.Monster, CardPosition.FaceUpAttack);
        state.Cards.AddRange(new[] { source, tribute, other });
        var projector = new SeatProjection(0);
        var view = projector.Project(state, new[] { new DuelAction { Id = "summon", Kind = DuelCommandKind.NormalSummon,
            Card = source.Ref, Slots = new List<int> { 2 }, SelectionCards = new List<CardRef> { tribute.Ref }, MinSelections = 1, MaxSelections = 1 } });
        var action = Assert.Single(view.Actions);
        var input = new SeatInput { ActionToken = action.ActionToken, Slot = 2,
            SelectionViewCardIds = action.SelectionViewCardIds.ToArray() };
        Assert.True(projector.TryResolveInput(state, input, out var command));
        Assert.Equal(new[] { 20 }, command.Cards);
        input.SelectionViewCardIds = new[] { view.Cards.Single(c => c.DefinitionId == "opponent").ViewCardId };
        Assert.False(projector.TryResolveInput(state, input, out _));
        input.SelectionViewCardIds = new[] { action.SelectionViewCardIds[0], action.SelectionViewCardIds[0] };
        Assert.False(projector.TryResolveInput(state, input, out _));
        input.SelectionViewCardIds = Array.Empty<string>();
        Assert.False(projector.TryResolveInput(state, input, out _));
    }

    static DuelCardState Card(int id, string definition, int seat, DuelZone zone,
        CardPosition position = CardPosition.FaceDown) => new DuelCardState
    {
        InstanceId = id, DefinitionId = definition, Owner = seat, Controller = seat,
        Zone = zone, Position = position, CurrentAtk = 1900, CurrentDef = 1200
    };
}
