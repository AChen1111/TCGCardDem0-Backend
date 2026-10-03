using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class CompleteRulePackageTests
{
    [Fact]
    public void TheProductionPackageHasNoMissingRuleForAnyConfiguredCard()
    {
        var cards = DuelCardCatalog.CreateDefault();
        var rules = CardRuleCatalog.CreateDefault(cards);
        var missing = rules.CheckDeckSupport(cards.Cards.Select(card => card.CardId));
        Assert.True(missing.Count == 0, string.Join(Environment.NewLine,
            missing.Select(rule => rule.CardId + " / " + rule.RuleId + " / " + rule.Kind)));
    }
}
