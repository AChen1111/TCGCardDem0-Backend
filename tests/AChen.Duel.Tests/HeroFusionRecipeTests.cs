using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class HeroFusionRecipeTests
{
    [Fact]
    public void SunriseRequiresTwoHeroMonstersWithDifferentCurrentAttributes()
    {
        var catalog = DuelCardCatalog.CreateDefault();
        var rules = CardRuleCatalog.CreateDefault(catalog).Get("22908820");
        var neos = Material(catalog, "89943723");
        var stratos = Material(catalog, "40044918");
        var blueEyes = Material(catalog, "89631139");
        Assert.True(rules.HasSummonRecipe);
        Assert.True(rules.MatchesSummonMaterials(catalog.Get("22908820"), new[] { neos, stratos }, catalog));
        Assert.False(rules.MatchesSummonMaterials(catalog.Get("22908820"), new[] { neos, blueEyes }, catalog));
        stratos.CurrentAttribute = neos.CurrentAttribute;
        Assert.False(rules.MatchesSummonMaterials(catalog.Get("22908820"), new[] { neos, stratos }, catalog));
    }

    [Theory]
    [InlineData("23204029", "58481572", "58481572")]
    [InlineData("32828466", "22908820", "40044918")]
    [InlineData("55171412", "89943723", "17955766")]
    [InlineData("56733747", "89943723", "93347961")]
    [InlineData("60461804", "89943723", "09411399")]
    [InlineData("93347961", "89943723", "40044918")]
    public void HeroRecipeAcceptsItsRequiredPairAndRejectsAnUnrelatedDragon(string target, string first, string second)
    {
        var catalog = DuelCardCatalog.CreateDefault();
        var rule = CardRuleCatalog.CreateDefault(catalog).Get(target);
        Assert.True(rule.MatchesSummonMaterials(catalog.Get(target), new[] { Material(catalog, first), Material(catalog, second) }, catalog));
        Assert.False(rule.MatchesSummonMaterials(catalog.Get(target), new[] { Material(catalog, first), Material(catalog, "89631139") }, catalog));
    }
    static DuelCardState Material(DuelCardCatalog catalog, string id)
    {
        var definition = catalog.Get(id);
        return new DuelCardState { DefinitionId = id, CurrentAttribute = definition.Attribute,
            CurrentRace = definition.Race, CurrentLevel = definition.Level, CurrentNameId = definition.OriginalNameId };
    }
}
