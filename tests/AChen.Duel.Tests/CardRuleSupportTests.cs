using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class CardRuleSupportTests
{
    [Fact]
    public void UpstartKeepsItsPublicAbilityWhenRegisteredThroughItsCardRules()
    {
        var rules = CardRuleCatalog.CreateDefault(DuelCardCatalog.CreateDefault());
        var card = rules.Get("70368879");
        Assert.Equal("70368879", card.CardId);
        Assert.Equal("70368879.1", Assert.Single(card.CreateAbilities()).AbilityId);
        Assert.True(card.Support.IsComplete);
        Assert.True(DuelAbilityRegistry.CreateDefault().TryGet("70368879.1", out _));
    }

    [Fact]
    public void UnsupportedCardCannotPassTheDeckSupportGateAndNormalMonstersCan()
    {
        var printed = DuelCardCatalog.CreateDefault();
        var implemented = CardRuleCatalog.CreateDefault(printed);
        var rules = new CardRuleCatalog(printed, new CardRules[] {
            implemented.Get("89631139"), implemented.Get("89943723"), implemented.Get("70368879"), new DeclaredIncompleteCard() });
        Assert.True(rules.Get("89631139").Support.IsComplete);
        Assert.Empty(rules.Get("89631139").CreateAbilities());
        Assert.Empty(rules.CheckDeckSupport(new[] { "89631139", "89943723", "70368879" }));
        var missing = rules.CheckDeckSupport(new[] { "38517737", "38517737" });
        Assert.NotEmpty(missing);
        Assert.All(missing, item => Assert.Equal("38517737", item.CardId));
        Assert.Contains(missing, item => item.Kind == CardRuleKind.SummonProcedure);
        Assert.Contains(missing, item => item.Kind == CardRuleKind.ActivatedAbility);
    }

    [Fact]
    public void AlternateArtSharesOneRuleClassAndRegistryUsesExactlyItsDeclaredAbilities()
    {
        var rules = CardRuleCatalog.CreateDefault(DuelCardCatalog.CreateDefault());
        Assert.Same(rules.Get("14558127"), rules.Get("14558128"));
        var registry = DuelAbilityRegistry.Create(rules);
        Assert.Equal(rules.Cards.SelectMany(card => card.CreateAbilities()).Select(ability => ability.AbilityId).OrderBy(id => id),
            registry.Handlers.Select(ability => ability.AbilityId).OrderBy(id => id));
        Assert.Contains(registry.Handlers, ability => ability.AbilityId == "14558127.1");
    }

    [Fact]
    public void EffectMonsterCannotDeclareCompleteSupportByProvidingAnEmptyClass()
    {
        Assert.Throws<InvalidOperationException>(() => new CardRuleCatalog(DuelCardCatalog.CreateDefault(),
            new CardRules[] { new EmptyEffectMonster() }));
    }

    [Theory]
    [InlineData(CardRuleKind.ActivatedAbility)]
    [InlineData(CardRuleKind.ContinuousRule)]
    public void ADeclaredImplementedEffectMustHaveAnActualRuleHandler(CardRuleKind kind)
    {
        Assert.Throws<InvalidOperationException>(() => new CardRuleCatalog(DuelCardCatalog.CreateDefault(),
            new CardRules[] { new FalselyCompleteCard(kind) }));
    }

    sealed class FalselyCompleteCard : CardRules
    {
        readonly CardRuleKind m_kind;
        public FalselyCompleteCard(CardRuleKind kind) { m_kind = kind; }
        public override string CardId => "38517737";
        public override CardRuleSupport Support => new CardRuleSupport(CardRuleRequirement.Done("38517737.3", m_kind));
    }

    sealed class EmptyEffectMonster : CardRules
    {
        public override string CardId => "38517737";
        public override CardRuleSupport Support => new CardRuleSupport();
    }
    sealed class DeclaredIncompleteCard : CardRules
    {
        public override string CardId => "38517737";
        public override CardRuleSupport Support => new CardRuleSupport(
            CardRuleRequirement.Missing("38517737.summon", CardRuleKind.SummonProcedure),
            CardRuleRequirement.Missing("38517737.3", CardRuleKind.ActivatedAbility));
    }
}
