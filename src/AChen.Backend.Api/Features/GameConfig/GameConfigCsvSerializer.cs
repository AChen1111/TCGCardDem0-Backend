using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;

namespace AChen.Backend.Api.Features.GameConfig;

public sealed class GameConfigCsvSerializer
{
    private static readonly string[] Header =
    [
        "Table", "Id", "Name", "ResourceKey", "PriceGold",
        "StartsAt", "EndsAt", "SortOrder", "IsEnabled"
    ];

    public byte[] Serialize(GameConfigDraftData data)
    {
        var csv = new StringBuilder();
        AppendRow(csv, Header);
        foreach (var avatar in data.Avatars.OrderBy(value => value.SortOrder).ThenBy(value => value.Id))
        {
            AppendRow(csv,
            [
                "Avatar",
                avatar.Id.ToString(CultureInfo.InvariantCulture),
                avatar.Name,
                avatar.ResourceKey,
                avatar.PriceGold.ToString(CultureInfo.InvariantCulture),
                avatar.StartsAt?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                avatar.EndsAt?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                avatar.SortOrder.ToString(CultureInfo.InvariantCulture),
                avatar.IsEnabled.ToString(CultureInfo.InvariantCulture)
            ]);
        }

        foreach (var wallpaper in data.Wallpapers.OrderBy(value => value.SortOrder).ThenBy(value => value.Id))
        {
            AppendRow(csv,
            [
                "Wallpaper",
                wallpaper.Id.ToString(CultureInfo.InvariantCulture),
                wallpaper.Name,
                wallpaper.ResourceKey,
                wallpaper.PriceGold.ToString(CultureInfo.InvariantCulture),
                wallpaper.StartsAt?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                wallpaper.EndsAt?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                wallpaper.SortOrder.ToString(CultureInfo.InvariantCulture),
                wallpaper.IsEnabled.ToString(CultureInfo.InvariantCulture)
            ]);
        }

        foreach (var cardPack in data.CardPacks.OrderBy(value => value.SortOrder).ThenBy(value => value.Id))
        {
            AppendRow(csv,
            [
                "CardPack",
                cardPack.Id.ToString(CultureInfo.InvariantCulture),
                cardPack.Title,
                cardPack.CoverResourceKey,
                cardPack.PriceGold.ToString(CultureInfo.InvariantCulture),
                cardPack.StartsAt?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                cardPack.EndsAt?.ToString("O", CultureInfo.InvariantCulture) ?? "",
                cardPack.SortOrder.ToString(CultureInfo.InvariantCulture),
                cardPack.IsEnabled.ToString(CultureInfo.InvariantCulture)
            ]);
        }

        return new UTF8Encoding(true).GetBytes(csv.ToString());
    }

    public GameConfigDraftData Deserialize(ReadOnlyMemory<byte> content)
    {
        if (content.IsEmpty)
        {
            throw Invalid("CSV 文件为空。");
        }

        var avatars = new List<AvatarConfigResponse>();
        var wallpapers = new List<WallpaperConfigResponse>();
        var cardPacks = new List<CardPackConfigResponse>();
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
                throw Invalid("CSV 表头无效，请使用后台导出的模板。");
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
                var allowsZeroId = table.Equals("Avatar", StringComparison.OrdinalIgnoreCase) ||
                    table.Equals("Wallpaper", StringComparison.OrdinalIgnoreCase);
                var id = ParseInt(row[1], "Id", parser.LineNumber, positive: !allowsZeroId);
                if (allowsZeroId && id < 0)
                {
                    throw Invalid($"CSV 第 {parser.LineNumber} 行 Id 不能为负数。");
                }
                var name = RestoreSpreadsheetValue(row[2]).Trim();
                var resourceKey = RestoreSpreadsheetValue(row[3]).Trim();
                var priceGold = ParseLong(row[4], "PriceGold", parser.LineNumber);
                var sortOrder = ParseInt(row[7], "SortOrder", parser.LineNumber, positive: false);
                var isEnabled = ParseBool(row[8], "IsEnabled", parser.LineNumber);
                if (table.Equals("Avatar", StringComparison.OrdinalIgnoreCase))
                {
                    avatars.Add(new AvatarConfigResponse(id, name, resourceKey, priceGold, sortOrder, isEnabled,
                        ParseDate(row[5], "StartsAt", parser.LineNumber),
                        ParseDate(row[6], "EndsAt", parser.LineNumber)));
                    continue;
                }

                if (table.Equals("Wallpaper", StringComparison.OrdinalIgnoreCase))
                {
                    wallpapers.Add(new WallpaperConfigResponse(id, name, resourceKey, priceGold, sortOrder, isEnabled,
                        ParseDate(row[5], "StartsAt", parser.LineNumber),
                        ParseDate(row[6], "EndsAt", parser.LineNumber)));
                    continue;
                }

                if (!table.Equals("CardPack", StringComparison.OrdinalIgnoreCase))
                {
                    throw Invalid($"CSV 第 {parser.LineNumber} 行 Table 只能是 Avatar、Wallpaper 或 CardPack。");
                }

                cardPacks.Add(new CardPackConfigResponse(
                    id,
                    name,
                    resourceKey,
                    priceGold,
                    ParseDate(row[5], "StartsAt", parser.LineNumber),
                    ParseDate(row[6], "EndsAt", parser.LineNumber),
                    sortOrder,
                    isEnabled));
            }
        }
        catch (MalformedLineException exception)
        {
            throw Invalid($"CSV 第 {exception.LineNumber} 行格式无效。");
        }

        return new GameConfigDraftData(avatars, wallpapers, cardPacks);
    }

    private static int ParseInt(string value, string field, long line, bool positive)
    {
        if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ||
            positive && parsed <= 0)
        {
            throw Invalid($"CSV 第 {line} 行 {field} 不是有效整数。");
        }

        return parsed;
    }

    private static long ParseLong(string value, string field, long line)
    {
        if (!long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            throw Invalid($"CSV 第 {line} 行 {field} 不是有效整数。");
        }

        return parsed;
    }

    private static bool ParseBool(string value, string field, long line)
    {
        if (!bool.TryParse(value, out var parsed))
        {
            throw Invalid($"CSV 第 {line} 行 {field} 必须是 True 或 False。");
        }

        return parsed;
    }

    private static DateTimeOffset? ParseDate(string value, string field, long line)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!DateTimeOffset.TryParse(
                value,
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind,
                out var parsed))
        {
            throw Invalid($"CSV 第 {line} 行 {field} 必须是 ISO-8601 时间。");
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

    private static GameConfigCsvException Invalid(string message) => new(message);
}

public sealed class GameConfigCsvException(string message)
    : Exception(message);
