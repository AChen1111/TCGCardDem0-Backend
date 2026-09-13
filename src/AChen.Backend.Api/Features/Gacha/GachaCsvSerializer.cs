using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace AChen.Backend.Api.Features.Gacha;

public sealed class GachaCsvSerializer
{
    private static readonly string[] Header = ["Table", "PoolKey", "CardId", "Weight", "Rarity"];

    public byte[] Serialize(GachaConfigData data)
    {
        var csv = new StringBuilder();
        AppendRow(csv, Header);
        foreach (var entry in data.PoolEntries
            .OrderBy(value => value.PoolKey, StringComparer.Ordinal)
            .ThenBy(value => value.CardId, StringComparer.Ordinal))
        {
            AppendRow(csv, ["Card", entry.PoolKey, entry.CardId, entry.Weight.ToString(CultureInfo.InvariantCulture), ""]);
        }

        foreach (var weight in data.RarityWeights.OrderBy(value => value.Rarity))
        {
            AppendRow(csv,
            [
                "Rarity",
                "",
                "",
                weight.Weight.ToString(CultureInfo.InvariantCulture),
                weight.Rarity.ToString(CultureInfo.InvariantCulture)
            ]);
        }

        return new UTF8Encoding(true).GetBytes(csv.ToString());
    }

    public GachaConfigData Deserialize(ReadOnlyMemory<byte> content)
    {
        if (content.IsEmpty)
        {
            throw Invalid("CSV 文件为空。");
        }

        var poolEntries = new List<GachaPoolEntryResponse>();
        var rarityWeights = new List<GachaRarityWeightResponse>();
        try
        {
            using var stream = new MemoryStream(content.ToArray());
            using var parser = new TextFieldParser(stream, Encoding.UTF8, true, false)
            {
                TextFieldType = FieldType.Delimited,
                HasFieldsEnclosedInQuotes = true,
                TrimWhiteSpace = false
            };
            parser.SetDelimiters(",");

            var header = parser.ReadFields();
            if (header is null || !header.SequenceEqual(Header, StringComparer.OrdinalIgnoreCase))
            {
                throw Invalid("CSV 表头无效，须为 Table,PoolKey,CardId,Weight,Rarity。");
            }

            while (!parser.EndOfData)
            {
                var row = parser.ReadFields();
                if (row is null || row.All(string.IsNullOrWhiteSpace))
                {
                    continue;
                }

                if (row.Length != Header.Length)
                {
                    throw Invalid($"CSV 第 {parser.LineNumber} 行字段数量不正确。");
                }

                var table = row[0].Trim();
                if (table.Equals("Card", StringComparison.OrdinalIgnoreCase))
                {
                    var poolKey = row[1].Trim();
                    var cardId = RestoreSpreadsheetValue(row[2]).Trim();
                    if (poolKey.Length is < 1 or > 32)
                    {
                        throw Invalid($"CSV 第 {parser.LineNumber} 行 PoolKey 长度须为 1-32。");
                    }

                    if (poolKey.Equals(GachaPoolKeys.AllCards, StringComparison.Ordinal))
                    {
                        throw Invalid($"CSV 第 {parser.LineNumber} 行 {GachaPoolKeys.AllCards} 不能写在抽卡权重表，请导入全部卡牌表。");
                    }

                    if (cardId.Length is < 1 or > 32)
                    {
                        throw Invalid($"CSV 第 {parser.LineNumber} 行 CardId 长度须为 1-32。");
                    }

                    if (!string.IsNullOrWhiteSpace(row[4]))
                    {
                        throw Invalid($"CSV 第 {parser.LineNumber} 行 Card 的 Rarity 须留空。");
                    }

                    poolEntries.Add(new GachaPoolEntryResponse(
                        poolKey,
                        cardId,
                        ParsePositiveInt(row[3], "Weight", parser.LineNumber)));
                    continue;
                }

                if (!table.Equals("Rarity", StringComparison.OrdinalIgnoreCase))
                {
                    throw Invalid($"CSV 第 {parser.LineNumber} 行 Table 只能是 Card 或 Rarity。");
                }

                if (!string.IsNullOrWhiteSpace(row[1]) || !string.IsNullOrWhiteSpace(row[2]))
                {
                    throw Invalid($"CSV 第 {parser.LineNumber} 行 Rarity 的 PoolKey 与 CardId 须留空。");
                }

                var rarity = ParseInt(row[4], "Rarity", parser.LineNumber);
                if (rarity is < 0 or > 4)
                {
                    throw Invalid($"CSV 第 {parser.LineNumber} 行 Rarity 须为 0-4。");
                }

                rarityWeights.Add(new GachaRarityWeightResponse(
                    rarity,
                    ParsePositiveInt(row[3], "Weight", parser.LineNumber)));
            }
        }
        catch (MalformedLineException exception)
        {
            throw Invalid($"CSV 第 {exception.LineNumber} 行格式无效。");
        }

        if (poolEntries.Count == 0)
        {
            throw Invalid("CSV 至少需要一行 Card。");
        }

        if (rarityWeights.Count == 0)
        {
            throw Invalid("CSV 至少需要一行 Rarity。");
        }

        if (poolEntries.GroupBy(value => (value.PoolKey, value.CardId)).Any(group => group.Count() > 1))
        {
            throw Invalid("同一卡池内 CardId 不能重复。");
        }

        if (rarityWeights.GroupBy(value => value.Rarity).Any(group => group.Count() > 1))
        {
            throw Invalid("稀有度权重不能重复。");
        }

        return new GachaConfigData(poolEntries, rarityWeights);
    }

    private static int ParsePositiveInt(string value, string field, long line)
    {
        var parsed = ParseInt(value, field, line);
        if (parsed <= 0)
        {
            throw Invalid($"CSV 第 {line} 行 {field} 必须为正整数。");
        }

        return parsed;
    }

    private static int ParseInt(string value, string field, long line)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw Invalid($"CSV 第 {line} 行 {field} 不是有效整数。");
        }

        return parsed;
    }

    private static void AppendRow(StringBuilder csv, IEnumerable<string> values) =>
        csv.AppendJoin(',', values.Select(Escape)).Append("\r\n");

    private static string Escape(string value)
    {
        value = ProtectSpreadsheetValue(value ?? "");
        return value.IndexOfAny([',', '"', '\r', '\n']) >= 0
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;
    }

    private static string ProtectSpreadsheetValue(string value) =>
        value.Length > 0 && value[0] is '=' or '+' or '-' or '@'
            ? "'" + value
            : value;

    private static string RestoreSpreadsheetValue(string value) =>
        value.Length > 1 && value[0] == '\'' && value[1] is '=' or '+' or '-' or '@'
            ? value[1..]
            : value;

    private static GachaCsvException Invalid(string message) => new(message);
}

public sealed class GachaCsvException(string message) : Exception(message);
