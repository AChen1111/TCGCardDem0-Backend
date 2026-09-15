// 后端仓库副本, 改协议时需与游戏仓库 Assets/Shared/Configuration/PublishedGameConfig.cs 同步.
using System;
using System.Collections.Generic;
using System.Linq;

namespace AChen.Configuration
{
    // Unity 与后台共享发布格式, 同一快照同时用于展示和结算.
    [Serializable]
    public sealed class PublishedGameConfig
    {
        public const string PackagePath = "GameConfig/config.json";
        public int SchemaVersion;
        public CatalogData Catalog;
        public PoolEntry[] PoolEntries;
        public RarityWeight[] RarityWeights;
        public AllCardEntry[] AllCards;
        public byte[] CardTable;
        public byte[] TranslationTable;
        public WallpaperOffset[] WallpaperOffsets;

        public void Validate()
        {
            if (SchemaVersion != 1 || Catalog == null || CardTable == null || TranslationTable == null)
                throw new FormatException("配置结构版本或必需数据无效");
            Unique(Catalog.Avatars, x => x.Id, "头像");
            Unique(Catalog.Wallpapers, x => x.Id, "壁纸");
            Unique(Catalog.CardPacks, x => x.Id, "卡包");
            Unique(Catalog.Avatars, x => x.ResourceKey, "头像资源键");
            Unique(Catalog.Wallpapers, x => x.ResourceKey, "壁纸资源键");
            foreach (var item in Catalog.Avatars.Concat(Catalog.Wallpapers))
            {
                if (item.Id < 0) throw new FormatException("外观 ID 无效");
                Text(item.Name, 64); Text(item.ResourceKey, 128);
                Sale(item.PriceGold, item.StartsAt, item.EndsAt);
            }
            Unique(PoolEntries, x => x.PoolKey + "/" + x.CardId, "卡池条目");
            Unique(RarityWeights, x => x.Rarity, "稀有度");
            Unique(AllCards, x => x.CardId, "全卡清单");
            if ((long)AllCards.Length * 100 > int.MaxValue) throw new FormatException("全卡池总权重超限");
            var cards = Table.CardRow.LoadBytes(CardTable);
            Unique(cards.ToArray(), x => x.CardId, "卡牌属性");
            var ids = new HashSet<string>(cards.Select(x => x.CardId), StringComparer.Ordinal);
            foreach (var card in AllCards)
            {
                Text(card.SourcePool, 32);
                if (!ids.Contains(card.CardId)) throw new FormatException("全卡清单缺少卡牌属性: " + card.CardId);
            }
            foreach (var pool in PoolEntries)
            {
                Text(pool.PoolKey, 32);
                if (pool.PoolKey == "CardAll" || pool.Weight <= 0 || !ids.Contains(pool.CardId))
                    throw new FormatException("卡池权重或卡牌引用无效: " + pool.CardId);
            }
            foreach (var group in PoolEntries.GroupBy(x => x.PoolKey))
                if (group.Sum(x => (long)x.Weight) > int.MaxValue) throw new FormatException("卡池总权重超限");
            if (RarityWeights.Length == 0 || RarityWeights.Any(x => x.Rarity < 0 || x.Rarity > 4 || x.Weight <= 0)
                || RarityWeights.Sum(x => (long)x.Weight) > int.MaxValue)
                throw new FormatException("稀有度权重无效");
            foreach (var pack in Catalog.CardPacks)
            {
                if (pack.Id <= 0) throw new FormatException("卡包 ID 无效");
                Text(pack.Title, 64); Text(pack.CoverResourceKey, 128); Text(pack.PoolKey, 32);
                Sale(pack.PriceGold, pack.StartsAt, pack.EndsAt);
                if (pack.PoolKey == "CardAll" ? AllCards.Length == 0 : !PoolEntries.Any(x => x.PoolKey == pack.PoolKey))
                    throw new FormatException("卡包引用不存在的卡池: " + pack.PoolKey);
            }
            var translations = Table.TranslationRow.LoadBytes(TranslationTable);
            Unique(translations.ToArray(), x => x.Key, "语言键");
            foreach (var row in translations)
            {
                Text(row.Key, 256);
                var chinese = Placeholders(row.Chinese);
                var english = Placeholders(row.English);
                if (!string.IsNullOrEmpty(row.Chinese) && !string.IsNullOrEmpty(row.English) && !chinese.SetEquals(english))
                    throw new FormatException("多语言占位符不一致: " + row.Key);
            }
            Unique(WallpaperOffsets, x => x.Id, "壁纸显示参数");
            foreach (var offset in WallpaperOffsets)
            {
                if (!Catalog.Wallpapers.Any(x => x.Id == offset.Id)
                    || offset.Sprite == null || offset.Down == null || offset.Sprite.Length != 3 || offset.Down.Length != 3
                    || offset.Sprite.Concat(offset.Down).Any(x => float.IsNaN(x) || float.IsInfinity(x)))
                    throw new FormatException("壁纸偏移或关联无效: " + offset.Id);
            }
        }

        static HashSet<string> Placeholders(string text)
        {
            var values = new HashSet<string>(StringComparer.Ordinal);
            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text ?? "", @"\{([A-Za-z][A-Za-z0-9_]*)\}"))
                values.Add(match.Groups[1].Value);
            return values;
        }

        static void Unique<T, TKey>(T[] values, Func<T, TKey> key, string label)
        {
            if (values == null || values.Any(x => ReferenceEquals(x, null))) throw new FormatException(label + "缺失");
            var keys = new HashSet<TKey>();
            if (values.Any(x => !keys.Add(key(x)))) throw new FormatException(label + "重复");
        }
        static void Text(string value, int max)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > max || value.Any(char.IsControl))
                throw new FormatException("配置文本无效: " + value);
        }
        static void Sale(long price, DateTimeOffset? starts, DateTimeOffset? ends)
        {
            if (price < 0 || starts.HasValue && ends.HasValue && ends <= starts) throw new FormatException("价格或上下架时间无效");
        }
    }
    [Serializable] public sealed class CatalogData
    {
        public CosmeticData[] Avatars;
        public CosmeticData[] Wallpapers;
        public PackData[] CardPacks;
    }
    [Serializable] public sealed class CosmeticData
    {
        public int Id;
        public string Name;
        public string ResourceKey;
        public long PriceGold;
        public int SortOrder;
        public bool IsEnabled;
        public DateTimeOffset? StartsAt;
        public DateTimeOffset? EndsAt;
    }
    [Serializable] public sealed class PackData
    {
        public int Id;
        public string Title;
        public string CoverResourceKey;
        public string PoolKey;
        public long PriceGold;
        public int SortOrder;
        public bool IsEnabled;
        public DateTimeOffset? StartsAt;
        public DateTimeOffset? EndsAt;
    }
    [Serializable] public sealed class PoolEntry { public string PoolKey; public string CardId; public int Weight; }
    [Serializable] public sealed class RarityWeight { public int Rarity; public int Weight; }
    [Serializable] public sealed class AllCardEntry { public string CardId; public string SourcePool; }
    [Serializable] public sealed class WallpaperOffset { public int Id; public float[] Sprite; public float[] Down; }
}
