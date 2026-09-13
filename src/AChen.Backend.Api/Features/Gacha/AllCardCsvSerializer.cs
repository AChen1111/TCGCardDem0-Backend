using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace AChen.Backend.Api.Features.Gacha;

public sealed class AllCardCsvSerializer
{
    private static readonly string[] Header = ["CardId", "SourcePool"];

    public byte[] Serialize(AllCardsData data)
    {
        var csv = new StringBuilder();
        AppendRow(csv, Header);
        foreach (var card in data.Cards
            .OrderBy(value => value.CardId, StringComparer.Ordinal))
        {
            AppendRow(csv, [card.CardId, card.SourcePool]);
        }

        return new UTF8Encoding(true).GetBytes(csv.ToString());
    }

    public AllCardsData Deserialize(ReadOnlyMemory<byte> content)
    {
        if (content.IsEmpty)
        {
            throw Invalid("CSV 文件为空。");
        }

        var cards = new List<AllCardResponse>();
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
                throw Invalid("CSV 表头无效，须为 CardId,SourcePool。");
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

                var cardId = RestoreSpreadsheetValue(row[0]).Trim();
                var sourcePool = row[1].Trim();
                if (cardId.Length is < 1 or > 32)
                {
                    throw Invalid($"CSV 第 {parser.LineNumber} 行 CardId 长度须为 1-32。");
                }

                if (sourcePool.Length is < 1 or > 32)
                {
                    throw Invalid($"CSV 第 {parser.LineNumber} 行 SourcePool 长度须为 1-32。");
                }

                if (sourcePool.Equals(GachaPoolKeys.AllCards, StringComparison.Ordinal))
                {
                    throw Invalid($"CSV 第 {parser.LineNumber} 行 SourcePool 不能是 {GachaPoolKeys.AllCards}。");
                }

                cards.Add(new AllCardResponse(cardId, sourcePool));
            }
        }
        catch (MalformedLineException exception)
        {
            throw Invalid($"CSV 第 {exception.LineNumber} 行格式无效。");
        }

        if (cards.Count == 0)
        {
            throw Invalid("CSV 至少需要一行卡牌。");
        }

        if (cards.GroupBy(value => value.CardId, StringComparer.Ordinal).Any(group => group.Count() > 1))
        {
            throw Invalid("CardId 不能重复。");
        }

        return new AllCardsData(cards);
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

    private static AllCardCsvException Invalid(string message) => new(message);
}

public sealed class AllCardCsvException(string message) : Exception(message);
