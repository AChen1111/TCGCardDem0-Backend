using AChen.Duel.Core;
using System;
using System.IO;
using System.Linq;
using Xunit;

namespace AChen.Duel.Tests;

public sealed class CardCatalogTests
{
    [Fact]
    public void BlueEyesUsesPrintedMonsterCharacteristicsWithoutInventingAnEffect()
    {
        var card = DuelCardCatalog.CreateDefault().Get("89631139");

        Assert.Equal("89631139", card.CardId);
        Assert.Equal(RuleCardKind.Monster, card.Kind);
        Assert.True(card.IsNormal);
        Assert.Equal(8, card.Level);
        Assert.Equal(3000, card.Attack);
        Assert.Equal(2500, card.Defense);
        Assert.Empty(card.Abilities);
    }

    [Fact]
    public void CatalogContainsExactlyTheConfiguredNinetyOneRuleCards()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "TableData", "Cards.csv"))) root = root.Parent!;
        var configured = File.ReadAllLines(Path.Combine(root.FullName, "TableData", "Cards.csv"))
            .Skip(2).Where(line => line.Length != 0).Select(line => line.Split(',')[0]).OrderBy(id => id);
        var catalog = DuelCardCatalog.CreateDefault();

        Assert.Equal(configured, catalog.Cards.Select(card => card.CardId).OrderBy(id => id));
        Assert.Equal(91, catalog.Cards.Count);
    }

    [Fact]
    public void SummonCharacteristicsDistinguishLevelRankAndLinkArrows()
    {
        var catalog = DuelCardCatalog.CreateDefault();
        var kagari = catalog.Get("63288574");
        Assert.Equal(RuleMonsterType.Link, kagari.MonsterType);
        Assert.Equal(0, kagari.Level);
        Assert.Equal(1, kagari.LinkRating);
        Assert.Equal(0x40, kagari.LinkArrows);
        Assert.Null(kagari.Defense);
        Assert.True(kagari.IsExtra);
        var felgrand = catalog.Get("01639384");
        Assert.Equal(8, felgrand.Rank);
        Assert.Equal(0, felgrand.Level);
        Assert.Equal(1800, felgrand.Defense);
        Assert.True(catalog.Get("08240199").IsTuner);
        Assert.Equal(16, catalog.Get("89631139").Attribute);
        Assert.Equal(8192, catalog.Get("89631139").Race);
    }

    [Fact]
    public void AlternateArtUsesTheSameRuleCardAndTheCurrentCopyLimit()
    {
        var catalog = DuelCardCatalog.CreateDefault();
        Assert.Equal("14558127", catalog.ResolveCardId("14558128"));
        Assert.Same(catalog.Get("14558127"), catalog.Get("14558128"));
        Assert.Equal(1, catalog.Get("14558128").MaxCopies);
        Assert.Equal(0, catalog.Get("23434538").MaxCopies);
        Assert.Equal(2, catalog.Get("21143940").MaxCopies);
        Assert.Equal(3, catalog.Get("89631139").MaxCopies);
        Assert.Equal("26077389", catalog.Get("26077389").CardId);
        Assert.Equal("26077387", catalog.Get("26077389").OriginalNameId);
    }

    [Fact]
    public void SearchFiltersUseStructuredSeriesAndSpellKindsInsteadOfDisplayNames()
    {
        var catalog = DuelCardCatalog.CreateDefault();
        Assert.True(catalog.Get("23204029").BelongsTo(0x3008)); // Contrast HERO is also Elemental HERO.
        Assert.True(catalog.Get("23204029").BelongsTo(0x0008));
        Assert.False(catalog.Get("23204029").BelongsTo(0xc008));
        Assert.True(catalog.Get("20357457").BelongsTo(0x115)); // Cyanos is always a Sky Striker card.
        Assert.False(catalog.Get("20357457").BelongsTo(0x1115));
        Assert.True(catalog.Get("17217034").BelongsTo(0x1115));
        Assert.Equal(RuleSpellTrapType.QuickPlay, catalog.Get("21143940").SpellTrapType);
        Assert.Equal(RuleSpellTrapType.Field, catalog.Get("50005218").SpellTrapType);
        Assert.Equal(RuleSpellTrapType.Normal, catalog.Get("49299410").SpellTrapType);
    }

    [Fact]
    public void EveryPrintedEffectHasAnIdentityWithoutTreatingFlavorTextAsExecutable()
    {
        var catalog = DuelCardCatalog.CreateDefault();
        Assert.All(catalog.Cards.Where(card => !card.IsNormal), card => Assert.NotEmpty(card.Abilities));
        Assert.Equal(new[] { "55063751.1", "55063751.2", "55063751.3", "55063751.4" },
            catalog.Get("55063751").Abilities.Select(ability => ability.Id));
        Assert.Contains("坏兽指示物", catalog.Get("55063751").Abilities[3].Text);
        Assert.Contains("回到持有者手卡", Assert.Single(catalog.Get("33698022").Abilities).Text);
        Assert.Contains("不会被战斗", Assert.Single(catalog.Get("83994433").Abilities).Text);
        Assert.Contains("db.yugioh-card.com", catalog.Get("25072579").OfficialSourceUrl);
        Assert.All(catalog.Cards.Where(card => card.IsNormal), card => Assert.Empty(card.Abilities));
    }

    [Fact]
    public void DeclarationCatalogIncludesCardsOutsideThePlayablePoolButExcludesTokensAndRuleAliases()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "TableData", "Cards.csv"))) root = root.Parent!;
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(Path.Combine(root.FullName, "TableData", "card-name-catalog.csv"));
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;
        parser.ReadFields(); parser.ReadFields();
        var names = new Dictionary<string, string[]>();
        while (!parser.EndOfData) { var row = parser.ReadFields()!; names.Add(row[0], row); }
        Assert.Contains("46986414", names.Keys); // Dark Magician: legal name, outside this playable pool.
        Assert.DoesNotContain("00176393", names.Keys); // Koa'ki Meiru Token.
        Assert.DoesNotContain("00295517", names.Keys); // A Legendary Ocean is named Umi by rule.
        Assert.Contains("22702055", names.Keys);
        Assert.Contains("26077387", names.Keys); // Canonical original name for project Raye art ID.
    }

    [Fact]
    public void CatalogPublishesAStableContentFingerprintForRoomVersionLocking()
    {
        var first = DuelCardCatalog.CreateDefault();
        Assert.Matches("^[0-9a-f]{64}$", first.Fingerprint);
        Assert.Equal(first.Fingerprint, DuelCardCatalog.CreateDefault().Fingerprint);
    }

    [Fact]
    public void AlternativeDragonCannotUseNormalSummonOrSetButBlueEyesCan()
    {
        var catalog = DuelCardCatalog.CreateDefault();
        Assert.False(catalog.Get("38517737").CanNormalSummon);
        Assert.False(catalog.Get("02129638").CanNormalSummon);
        Assert.False(catalog.Get("63288574").CanNormalSummon);
        Assert.False(catalog.Get("00213326").CanNormalSummon);
        Assert.True(catalog.Get("89631139").CanNormalSummon);
    }
}
