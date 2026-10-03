using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class CardNameCatalogTests
{
    [Fact]
    public void DeclarationUsesPublicNameDataOutsideThePlayablePoolWithoutTokensOrAliases()
    {
        var names = CardNameCatalog.CreateDefault();
        Assert.Equal("Dark Magician", names.Get("46986414").English);
        Assert.Contains(names.ForKind(RuleCardKind.Monster), name => name.NameId == "46986414");
        Assert.DoesNotContain(names.ForKind(RuleCardKind.Spell), name => name.NameId == "46986414");
        Assert.False(names.Contains("00176393"));
        Assert.False(names.Contains("00295517"));
        Assert.True(names.Contains("26077387"));
        Assert.Matches("^[0-9a-f]{64}$", names.Fingerprint);
        Assert.Equal(names.Fingerprint, CardNameCatalog.CreateDefault().Fingerprint);
    }
}
