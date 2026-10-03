using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class CardActivationFlowTests
{
    [Fact]
    public void NegatedActivationOfASetPersistentSpellIsSentToGraveInsteadOfItsLeaveFieldBanishment()
    {
        var spell = new ProgramAbility("50005218", 99, 1, new[] { DuelZone.Field }, c => true, (c, l) => { });
        var negate = new ProgramAbility("97268402", 99, 2, new[] { DuelZone.Hand }, c => true,
            (c, l) => c.State.Chain[l.Number - 2].ActivationNegated = true);
        var duel = Create(spell.CardId, spell, negate);
        // 夹具建立由多任务效果盖放并带有离场除外状态的场地卡。
        var source = duel.State.Cards.Single(c => c.InstanceId == 1);
        source.Zone = DuelZone.Field; source.Position = CardPosition.FaceDown;
        duel.State.Effects.Add(new DuelEffectRecord { Id = duel.State.NextEffectId++, Kind = EffectRecordKind.BanishWhenLeavesField, Target = source.Ref });
        Activate(duel, 0, 1, spell.AbilityId); Activate(duel, 1, 41, negate.AbilityId);
        Pass(duel); Pass(duel);
        Assert.Equal(DuelZone.Graveyard, source.Zone);
    }

    [Theory]
    [InlineData("24010609")]
    [InlineData("50005218")]
    public void NegatingTheCardActivationSendsPersistentSpellToGraveyard(string definitionId)
    {
        // 专用处理器仅隔离公共的卡片发动清理规则，不代表该卡真实效果。
        var spell = new ProgramAbility(definitionId, 99, 1,
            new[] { DuelZone.Hand, DuelZone.SpellTrap, DuelZone.Field }, c => true, (c, l) => { });
        var negate = new ProgramAbility("97268402", 99, 2, new[] { DuelZone.Hand },
            c => c.State.Chain.Count > 0, (c, l) => c.State.Chain[l.Number - 2].ActivationNegated = true);
        var duel = Create(definitionId, spell, negate);
        Activate(duel, 0, 1, spell.AbilityId);
        Activate(duel, 1, 41, negate.AbilityId);
        Pass(duel); Pass(duel);
        Assert.Equal(DuelZone.Graveyard, duel.State.Cards.Single(c => c.InstanceId == 1).Zone);
    }

    [Theory]
    [InlineData("24010609")]
    [InlineData("50005218")]
    public void PersistentSpellRemovedByTheChainedEffectCannotApplyItsResolution(string definitionId)
    {
        var spell = new ProgramAbility(definitionId, 99, 1,
            new[] { DuelZone.Hand, DuelZone.SpellTrap, DuelZone.Field }, c => true,
            (c, l) => c.State.Players[0].LifePoints += 100);
        var remove = new ProgramAbility("97268402", 99, 2, new[] { DuelZone.Hand },
            c => c.State.Chain.Count > 0, (c, l) => c.Move(c.Card(1), DuelZone.Graveyard));
        var duel = Create(definitionId, spell, remove);
        Activate(duel, 0, 1, spell.AbilityId);
        Activate(duel, 1, 41, remove.AbilityId);
        Pass(duel); Pass(duel);
        Assert.Equal(8000, duel.State.Players[0].LifePoints);
    }

    [Fact]
    public void NegatingAnAlreadyFaceUpContinuousSpellsEffectKeepsTheCardOnField()
    {
        var spell = new ProgramAbility("24010609", 99, 1,
            new[] { DuelZone.Hand, DuelZone.SpellTrap }, c => true, (c, l) => { });
        var negate = new ProgramAbility("97268402", 99, 2, new[] { DuelZone.Hand },
            c => c.State.Chain.Count > 0, (c, l) => c.State.Chain[l.Number - 2].ActivationNegated = true);
        var duel = Create(spell.CardId, spell, negate);
        Activate(duel, 0, 1, spell.AbilityId);
        Pass(duel); Pass(duel);
        Pass(duel); Pass(duel);
        Activate(duel, 0, 1, spell.AbilityId);
        Activate(duel, 1, 41, negate.AbilityId);
        Pass(duel); Pass(duel);
        Assert.Equal(DuelZone.SpellTrap, duel.State.Cards.Single(c => c.InstanceId == 1).Zone);
    }

    static DuelEngine Create(string spellId, params IAbilityHandler[] handlers)
    {
        var record = new DuelStartRecord { Shuffle = false, MainDecks = new[] {
            new[] { spellId }.Concat(Enumerable.Repeat("89631139", 39)).ToArray(),
            new[] { "97268402" }.Concat(Enumerable.Repeat("89631139", 39)).ToArray() } };
        var duel = new DuelEngine(DuelCardCatalog.CreateDefault(), record, new DuelAbilityRegistry(handlers));
        while (duel.State.Phase != DuelPhase.Main1) Pass(duel);
        return duel;
    }

    static void Activate(DuelEngine duel, int player, int card, string ability) => Assert.True(duel.Apply(
        new DuelCommand { Kind = DuelCommandKind.Activate, Player = player, CardId = card, AbilityId = ability }).Accepted);
    static void Pass(DuelEngine duel) => Assert.True(duel.Apply(new DuelCommand {
        Kind = DuelCommandKind.Pass, Player = duel.State.WaitingSeat }).Accepted);
}
