using AChen.Duel.Core;

namespace AChen.Duel.Tests;

public sealed class CatalogIdentityTests
{
    [Fact]
    public void TheEntireRuntimeNameCatalogMatchesTheVersionedSourceTable()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "TableData", "card-name-catalog.csv"))) root = root.Parent!;
        using var parser = new Microsoft.VisualBasic.FileIO.TextFieldParser(Path.Combine(root.FullName, "TableData", "card-name-catalog.csv"));
        parser.SetDelimiters(","); parser.HasFieldsEnclosedInQuotes = true;
        parser.ReadFields(); parser.ReadFields();
        var expected = new List<(string, int, string, string, string)>();
        while (!parser.EndOfData)
        {
            var row = parser.ReadFields()!;
            expected.Add((row[0], int.Parse(row[1], System.Globalization.CultureInfo.InvariantCulture), row[2], row[3], row[4]));
        }
        var actual = CardNameCatalog.CreateDefault().Names.Select(n => (n.NameId, (int)n.Kind, n.Japanese, n.Chinese, n.English));
        Assert.Equal(expected.OrderBy(n => n.Item1, StringComparer.Ordinal), actual.OrderBy(n => n.NameId, StringComparer.Ordinal));
    }

    [Fact]
    public void DefaultCatalogUsesTheIdentityOfTheCompleteGeneratedPayload()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "Tools", "Duel", "card-catalog-manifest.json"))) root = root.Parent!;
        using var manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(root.FullName, "Tools", "Duel", "card-catalog-manifest.json")));
        var aliases = File.ReadAllLines(Path.Combine(root.FullName, "TableData", "card-art-variants.csv")).Skip(2)
            .Where(line => line.Length != 0).Select(line => line.Split(',')).ToDictionary(row => row[1], row => row[0]);
        var catalog = DuelCardCatalog.CreateDefault();
        string expected = DuelCardDataDigest.Compute(catalog.Cards, aliases,
            manifest.RootElement.GetProperty("sourceFingerprint").GetString()!);
        Assert.Equal(expected, catalog.Fingerprint);
        Assert.Equal(expected, manifest.RootElement.GetProperty("fingerprint").GetString());
    }

    [Fact]
    public void GeneratedCharacteristicChangeCreatesADifferentCatalogIdentityEvenWithTheSameSourceFiles()
    {
        var printed = DuelCardCatalog.CreateDefault();
        var original = printed.Get("89631139");
        var changed = new CardDefinition(original.CardId, original.Kind, original.IsNormal, original.Level,
            original.Attack + 1, original.Defense, original.MonsterType, original.IsTuner, original.Attribute,
            original.Race, original.Rank, original.LinkRating, original.LinkArrows, original.OriginalNameId,
            original.MaxCopies, original.SetCodes.ToArray(), original.SpellTrapType, original.Name, original.RulesText,
            original.MaterialText, original.OfficialSourceUrl, original.Abilities.ToArray(), original.CanNormalSummon);
        var aliases = new Dictionary<string, string> { ["14558128"] = "14558127" };
        string first = DuelCardDataDigest.Compute(printed.Cards, aliases, "identical-source");
        string second = DuelCardDataDigest.Compute(printed.Cards.Select(c => c.CardId == original.CardId ? changed : c), aliases, "identical-source");
        Assert.NotEqual(first, second);
    }
}
