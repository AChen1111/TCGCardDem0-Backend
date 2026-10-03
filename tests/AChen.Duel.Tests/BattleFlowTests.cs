using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class BattleFlowTests
{
    [Fact]
    public void ExtraAttacksRestrictedToEachMonsterOnceCannotRepeatTheSurvivingTarget()
    {
        var duel = Battle();
        var attacker = duel.State.Cards.Single(c => c.Owner == 0);
        var defender = duel.State.Cards.Single(c => c.Owner == 1);
        defender.Zone = DuelZone.Monster; defender.Position = CardPosition.FaceUpAttack;
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.ExtraMonsterAttacks, Target = attacker.Ref, Value = 1 });
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.EachMonsterOnce, Target = attacker.Ref });
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.BattleIndestructible, Target = defender.Ref });
        var attack = new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0,
            CardId = attacker.InstanceId, TargetId = defender.InstanceId };
        Assert.True(duel.Apply(attack).Accepted);
        for (int i = 0; i < 20 && duel.State.BattleStep != BattleStep.None; i++) Pass(duel);
        Assert.Equal(DuelZone.Monster, defender.Zone);
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.Kind == DuelCommandKind.Attack);
        Assert.False(duel.Apply(attack).Accepted);
        Assert.Equal(1, attacker.AttacksThisTurn);
    }

    [Fact]
    public void BattleDestructionCanBeReplacedByBanishmentWithoutUndoingBattleDamage()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "79814787", "06853254" }, new[] { "89631139" } },
            OpeningHand = 0, Shuffle = false, FirstPlayer = 1
        }, new DuelAbilityRegistry(Array.Empty<IAbilityHandler>()));
        foreach (var card in duel.State.Cards)
        {
            duel.State.Players[card.Owner].Deck.Clear(); card.Position = CardPosition.FaceUpAttack;
            card.Zone = card.DefinitionId == "06853254" ? DuelZone.Graveyard : DuelZone.Monster;
        }
        duel.State.Turn = 2; duel.State.Phase = DuelPhase.Battle; duel.State.Window = TimingWindow.Open;
        var attacker = duel.State.Cards.Single(c => c.Owner == 1);
        var defender = duel.State.Cards.Single(c => c.DefinitionId == "79814787");
        var lord = duel.State.Cards.Single(c => c.DefinitionId == "06853254");
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 1,
            CardId = attacker.InstanceId, TargetId = defender.InstanceId });
        Assert.True(result.Accepted, result.Error);
        for (int i = 0; i < 20 && duel.State.PendingDecision == null && duel.State.BattleStep != BattleStep.None; i++) Pass(duel);
        var decision = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal("destruction.replace", decision.Continuation);
        Assert.Equal(5300, duel.State.Players[0].LifePoints);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0,
            DecisionId = decision.Id, Options = new[] { decision.Options.Single(o => o.HasCard).Id } }).Accepted);
        for (int i = 0; i < 20 && duel.State.BattleStep != BattleStep.None; i++) Pass(duel);
        Assert.Equal(DuelZone.Monster, defender.Zone);
        Assert.Equal(DuelZone.Banished, lord.Zone);
        Assert.Equal(5300, duel.State.Players[0].LifePoints);
    }

    [Fact]
    public void ExtraMonsterAttackAllowsASecondTargetedAttackButNeverAnotherDirectAttack()
    {
        var duel = Battle();
        var attacker = duel.State.Cards.Single(c => c.Owner == 0);
        var defender = duel.State.Cards.Single(c => c.Owner == 1);
        defender.Zone = DuelZone.Monster; defender.Position = CardPosition.FaceUpAttack;
        attacker.AttacksThisTurn = 1;
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.ExtraMonsterAttacks,
            Target = attacker.Ref, Value = 1 });
        Assert.Contains(duel.QueryLegalActions(0), a => a.Kind == DuelCommandKind.Attack && !a.CanAttackDirectly);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0,
            CardId = attacker.InstanceId, TargetId = defender.InstanceId }).Accepted);
        for (int i = 0; i < 20 && duel.State.BattleStep != BattleStep.None; i++) Pass(duel);
        Assert.Equal(2, attacker.AttacksThisTurn);
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0,
            CardId = attacker.InstanceId }).Accepted);
    }

    [Fact]
    public void DirectAttackPermissionAllowsDamageEvenWhileOpponentHasAMonster()
    {
        var duel = Battle();
        var attacker = duel.State.Cards.Single(c => c.Owner == 0);
        var defender = duel.State.Cards.Single(c => c.Owner == 1);
        defender.Zone = DuelZone.Monster; defender.Position = CardPosition.FaceUpDefense;
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.DirectAttack, Target = attacker.Ref });
        Assert.Contains(duel.QueryLegalActions(0), a => a.Kind == DuelCommandKind.Attack && a.CanAttackDirectly);
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0, CardId = attacker.InstanceId });
        Assert.True(result.Accepted, result.Error);
        for (int i = 0; i < 20 && duel.State.BattleStep != BattleStep.None; i++) Pass(duel);
        Assert.Equal(6200, duel.State.Players[1].LifePoints);
        Assert.Equal(DuelZone.Monster, defender.Zone);
    }

    [Fact]
    public void AttackRestrictionRemovesTheActionAndRejectsAttackCommands()
    {
        var duel = Battle();
        var attacker = duel.State.Cards.Single(c => c.Owner == 0);
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.CannotAttack, Target = attacker.Ref });
        Assert.DoesNotContain(duel.QueryLegalActions(0), a => a.Kind == DuelCommandKind.Attack);
        Assert.False(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0,
            CardId = attacker.InstanceId }).Accepted);
        Assert.Equal(0, attacker.AttacksThisTurn);
    }

    [Fact]
    public void PiercingAppliesTheAttackDefenseDifferenceAsBattleDamage()
    {
        var duel = Battle();
        var attacker = duel.State.Cards.Single(c => c.Owner == 0);
        var defender = duel.State.Cards.Single(c => c.Owner == 1);
        defender.Zone = DuelZone.Monster; defender.Position = CardPosition.FaceUpDefense;
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.Piercing, Target = attacker.Ref });
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0,
            CardId = attacker.InstanceId, TargetId = defender.InstanceId }).Accepted);
        for (int i = 0; i < 20 && duel.State.BattleStep != BattleStep.None; i++) Pass(duel);
        Assert.Equal(7700, duel.State.Players[1].LifePoints);
        Assert.Equal(DuelZone.Graveyard, defender.Zone);
    }

    [Fact]
    public void BattleDestructionProtectionKeepsMonsterOnFieldWhileBattleDamageStillApplies()
    {
        var duel = Battle();
        var attacker = duel.State.Cards.Single(c => c.Owner == 0);
        var defender = duel.State.Cards.Single(c => c.Owner == 1);
        defender.Zone = DuelZone.Monster; defender.Position = CardPosition.FaceUpAttack;
        duel.State.Effects.Add(new DuelEffectRecord { Kind = EffectRecordKind.BattleIndestructible, Target = defender.Ref });
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0,
            CardId = attacker.InstanceId, TargetId = defender.InstanceId }).Accepted);
        for (int i = 0; i < 20 && duel.State.BattleStep != BattleStep.None; i++) Pass(duel);
        Assert.Equal(7700, duel.State.Players[1].LifePoints);
        Assert.Equal(DuelZone.Monster, defender.Zone);
    }

    [Fact]
    public void NewDefenderDuringAttackResponseRequiresReplayInsteadOfContinuingDirectAttack()
    {
        var duel = Battle();
        var attacker = duel.State.Cards.Single(c => c.Owner == 0);
        var defender = duel.State.Cards.Single(c => c.Owner == 1);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Attack, Player = 0,
            CardId = attacker.InstanceId }).Accepted);
        Pass(duel);
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Activate, Player = 1,
            CardId = defender.InstanceId, AbilityId = "test.defender" }).Accepted);
        for (int i = 0; i < 20 && duel.State.PendingDecision == null && duel.State.BattleStep == BattleStep.Declaration; i++) Pass(duel);
        var choice = Assert.IsType<DuelDecision>(duel.State.PendingDecision);
        Assert.Equal(0, choice.Player);
        Assert.Contains(choice.Options, o => o.HasCard && o.Card.Equals(defender.Ref));
        Assert.True(duel.Apply(new DuelCommand { Kind = DuelCommandKind.Answer, Player = 0,
            DecisionId = choice.Id, Options = new[] { choice.Options.Single(o => o.HasCard).Id } }).Accepted);
        for (int i = 0; i < 20 && duel.State.BattleStep != BattleStep.None; i++) Pass(duel);
        Assert.Equal(8000, duel.State.Players[1].LifePoints);
        Assert.Equal(DuelZone.Graveyard, defender.Zone);
        Assert.Equal(1, attacker.AttacksThisTurn);
    }

    static DuelEngine Battle()
    {
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), new DuelStartRecord
        {
            MainDecks = new[] { new[] { "40044918" }, new[] { "26077389" } },
            OpeningHand = 0, Shuffle = false
        }, new DuelAbilityRegistry(new[] { new SummonDefender() }));
        duel.State.Turn = 2; duel.State.Phase = DuelPhase.Battle; duel.State.Window = TimingWindow.Open;
        foreach (var card in duel.State.Cards)
        {
            duel.State.Players[card.Owner].Deck.Clear();
            card.Zone = card.Owner == 0 ? DuelZone.Monster : DuelZone.Hand;
            card.Position = CardPosition.FaceUpAttack;
        }
        return duel;
    }

    static void Pass(DuelEngine duel)
    {
        var result = duel.Apply(new DuelCommand { Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat });
        Assert.True(result.Accepted, result.Error);
    }

    sealed class SummonDefender : IAbilityHandler
    {
        public string CardId => "26077389";
        public string AbilityId => "test.defender";
        public int Speed => 2;
        public bool CanActivate(EffectContext context) => context.Source.Zone == DuelZone.Hand && context.Player == 1;
        public string ValidateActivation(EffectContext context, DuelCommand command) => "";
        public void PayCost(EffectContext context, DuelCommand command, DuelChainLink link) { }
        public void Resolve(EffectContext context, DuelChainLink link) =>
            context.SpecialSummon(context.Source, context.Player, 0, CardPosition.FaceUpDefense);
    }
}
