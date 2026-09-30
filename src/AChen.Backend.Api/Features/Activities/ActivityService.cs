using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AChen.Configuration;
using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Features.Players;
using AChen.Backend.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.Activities;

public sealed record ActivityClaimResponse(PlayerResponse Player, ActivityListResponse Activities, long GoldGained, long UrGained, IReadOnlyList<CardGrantResult> Cards);
public sealed class ActivityService(AppDbContext db, PublishedConfigReader reader, LatestContentService content,
    IPlayerRepository players, TimeProvider clock, ActivityGate gate, IHttpContextAccessor accessor)
{
    internal static readonly JsonSerializerOptions Json = LatestContentService.Json;
    static T Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)!;
    static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    static ApiException Error(string code, string text, int status = 409) => new(status, code, text);
    public static string Day(DateTimeOffset now) => now.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    static string Period(ActivityPeriodKind kind, DateTimeOffset now) => kind == ActivityPeriodKind.Daily ? Day(now) : "all";
    string Target => accessor.HttpContext!.Request.Headers["X-Content-Target"].ToString();
    async Task<PlayerProfile> Player(Guid id, CancellationToken ct) => await players.GetOrCreateAsync(id, clock.GetUtcNow(), ct)
        ?? throw Error("INVALID_ACCESS_TOKEN", "登录状态已失效", 401);

    async Task<T> Write<T>(Func<Task<T>> action, CancellationToken ct)
    {
        await gate.Mutex.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var result = await action();
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateException) { throw Error("PLAYER_DATA_CHANGED", "数据已变化，请刷新后重试"); }
        catch (Microsoft.Data.Sqlite.SqliteException error) when (error.SqliteErrorCode is 5 or 6)
        { throw Error("PLAYER_DATA_CHANGED", "数据正在更新，请刷新后重试"); }
        finally { gate.Mutex.Release(); }
    }

    public async Task<ActivityListResponse> ListAsync(Guid id, bool visit, CancellationToken ct)
    {
        await reader.GetAsync(ct);
        if (visit) return await Write(async () =>
        {
            var profile = await Player(id, ct);
            var now = clock.GetUtcNow();
            var states = await Build(profile, now, ct);
            foreach (var state in states.Activities.Where(x => x.Definition.Type == ActivityType.SignIn && x.Status == "running" && x.Eligible))
            {
                var key = Day(now);
                if (await db.Set<PlayerActivityVisit>().AnyAsync(x => x.PlayerId == id && x.ActivityId == state.Definition.Id && x.ServerDay == key, ct)) continue;
                db.Add(new PlayerActivityVisit { PlayerId = id, ActivityId = state.Definition.Id, ServerDay = key });
                var progress = await Progress(id, state.Definition.Id, ct);
                progress.Value = Math.Min(state.Definition.RequiredDays, progress.Value + 1);
                progress.Revision++;
            }
            await db.SaveChangesAsync(ct);
            return await Build(profile, now, ct);
        }, ct);
        await gate.Mutex.WaitAsync(ct);
        try { return await Build(await Player(id, ct), clock.GetUtcNow(), ct); }
        finally { gate.Mutex.Release(); }
    }

    async Task<PlayerActivityProgress> Progress(Guid player, string activity, CancellationToken ct)
    {
        var row = await db.Set<PlayerActivityProgress>().FindAsync(new object[] { player, activity }, ct);
        if (row != null) return row;
        row = new PlayerActivityProgress { PlayerId = player, ActivityId = activity };
        db.Add(row);
        return row;
    }

    async Task<List<ActivityDefinition>> Definitions(CancellationToken ct)
    {
        var records = await db.Set<ActivityDefinitionRecord>().AsNoTracking()
            .Where(x => x.PublishStatus == 1 && x.Target == Target).ToListAsync(ct);
        var ids = records.Select(x => x.Id).ToArray();
        var versions = await db.Set<ActivityPublishedVersion>().AsNoTracking().Where(x => ids.Contains(x.ActivityId)).ToListAsync(ct);
        var config = await reader.GetAsync(ct);
        var names = Table.TranslationRow.LoadBytes(config.TranslationTable).Where(x => x.Chinese.Length > 0 && x.English.Length > 0).Select(x => x.Key).ToHashSet(StringComparer.Ordinal);
        var resources = ResourceKeys(config);
        bool Compatible(ActivityDefinition d) => names.Contains(d.NameKey) && (d.DescriptionKey.Length == 0 || names.Contains(d.DescriptionKey)) &&
            (d.BannerResourceKey.Length == 0 || resources.Contains(d.BannerResourceKey)) && (d.Notice.ImageResourceKey.Length == 0 || resources.Contains(d.Notice.ImageResourceKey)) &&
            d.Entries.All(e => names.Contains(e.NameKey) && e.Rewards.All(r => r.RewardType != ActivityRewardType.Card || config.AllCards.Any(c => c.CardId == config.ResolveCardId(r.RewardId))));
        return records.Select(x => Parse<ActivityDefinition>(versions.Single(v => v.ActivityId == x.Id && v.Version == x.ActiveVersion).DefinitionJson)).Where(Compatible).ToList();
    }

    async Task<ActivityListResponse> Build(PlayerProfile player, DateTimeOffset now, CancellationToken ct)
    {
        var definitions = await Definitions(ct);
        var progress = await db.Set<PlayerActivityProgress>().AsNoTracking().Where(x => x.PlayerId == player.UserId).ToListAsync(ct);
        var counters = await db.Set<PlayerActivityCounter>().AsNoTracking().Where(x => x.PlayerId == player.UserId).ToListAsync(ct);
        var shown = await db.Set<PlayerActivityPopup>().AsNoTracking().Where(x => x.PlayerId == player.UserId).ToListAsync(ct);
        var result = new ActivityListResponse
        {
            ServerTime = now, ServerDay = Day(now), NextResetAt = DateTimeOffset.Parse(Day(now) + "T00:00:00+08:00", CultureInfo.InvariantCulture).AddDays(1),
            DefinitionsRevision = await db.Set<ActivityPublishedVersion>().LongCountAsync(ct),
            PlayerStateRevision = player.Revision + progress.Sum(x => x.Revision)
        };
        foreach (var definition in definitions.OrderBy(x => x.SortOrder).ThenBy(x => x.Id, StringComparer.Ordinal))
        {
            var state = new ActivitySnapshot { Definition = definition };
            state.Status = definition.ScheduleMode == ActivityScheduleMode.Permanent ? "running" : now < definition.StartsAt ? "upcoming" : now >= definition.EndsAt ? "ended" : "running";
            state.Eligible = Eligible(definition, player, progress);
            if (!state.Eligible)
            {
                state.LockedCondition = definition.OpenConditions.AllOf.First(c => !Meets(c, player, progress));
                state.LockedReasonCode = state.LockedCondition.Type;
            }
            var p = progress.FirstOrDefault(x => x.ActivityId == definition.Id);
            state.PlayerState.Progress = p?.Value ?? 0;
            state.PlayerState.Completed = p?.Completed ?? false;
            foreach (var entry in definition.Entries.OrderBy(x => x.SortOrder).ThenBy(x => x.Id, StringComparer.Ordinal))
            {
                var period = Period(entry.PeriodKind, now);
                int count = counters.FirstOrDefault(x => x.ActivityId == definition.Id && x.EntryId == entry.Id && x.PeriodKey == period)?.Count ?? 0;
                int total = counters.FirstOrDefault(x => x.ActivityId == definition.Id && x.EntryId == entry.Id && x.PeriodKey == "all")?.Count ?? 0;
                bool unlocked = definition.Type switch
                {
                    ActivityType.SignIn => state.PlayerState.Progress >= entry.DayIndex && (definition.AllowCatchUpClaims || state.PlayerState.Progress == entry.DayIndex),
                    ActivityType.Milestone => state.PlayerState.Progress >= entry.Threshold,
                    _ => true
                };
                bool limited = count >= entry.LimitPerPeriod || entry.TotalLimit.HasValue && total >= entry.TotalLimit;
                bool can = state.Status == "running" && state.Eligible && unlocked && !limited;
                state.PlayerState.EntryStates.Add(new ActivityEntryState { EntryId = entry.Id, PeriodKey = period, ClaimedCount = count, TotalClaimedCount = total,
                    CanClaim = can, Status = limited ? "claimed" : can ? "claimable" : "locked" });
            }
            if (definition.Type != ActivityType.Notice)
                state.PlayerState.Completed = definition.Entries.Count > 0 && definition.Entries.All(e => e.TotalLimit.HasValue &&
                    state.PlayerState.EntryStates.Single(s => s.EntryId == e.Id).TotalClaimedCount >= e.TotalLimit);
            var popupPeriod = definition.Popup.Frequency == "oncePerDay" ? Day(now) : "all";
            bool done = state.PlayerState.Completed || definition.Type != ActivityType.Notice && state.PlayerState.EntryStates.All(x => x.Status == "claimed");
            definition.Popup.ShouldShow = state.Status == "running" && state.Eligible && definition.DisplayMode != ActivityDisplayMode.Page &&
                !(definition.Popup.StopWhenCompleted && done) &&
                (definition.Popup.Trigger == "lobbyReady" || state.PlayerState.EntryStates.Any(x => x.CanClaim)) &&
                !shown.Any(x => x.ActivityId == definition.Id && x.PolicyVersion == definition.Popup.PolicyVersion && x.PeriodKey == popupPeriod);
            if (state.Status == "ended" || state.Status == "upcoming" && !definition.ShowBeforeStart || !state.Eligible && !definition.ShowLocked || state.PlayerState.Completed && definition.HideWhenCompleted) continue;
            result.Activities.Add(state);
        }
        return result;
    }

    static bool Eligible(ActivityDefinition definition, PlayerProfile profile, List<PlayerActivityProgress> progress) => definition.OpenConditions.AllOf.All(c => Meets(c, profile, progress));
    static bool Meets(ActivityCondition c, PlayerProfile profile, List<PlayerActivityProgress> progress) => c.Type switch
    {
        "ownedCardKindsAtLeast" => profile.OwnedCards.Where(x => x.Count > 0).Select(x => x.CardId).Distinct().Count() >= c.Value,
        "activityCompleted" => progress.Any(x => x.ActivityId == c.ActivityId && x.Completed),
        "playerCreatedBefore" => profile.CreatedAt < c.Time,
        "playerCreatedAfter" => profile.CreatedAt >= c.Time,
        _ => false
    };

    public Task<ActivityClaimResponse> ClaimAsync(Guid playerId, string id, ActivityClaimRequest request, bool exchange, CancellationToken ct) => Write(async () =>
    {
        if (!Guid.TryParse(request.RequestId, out _)) throw Error("INVALID_REQUEST_ID", "请求标识无效", 422);
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Encode(new { id, exchange, request }))));
        var previous = await db.Set<ActivityOperation>().FindAsync(new object[] { playerId, request.RequestId }, ct);
        if (previous != null)
        {
            if (previous.PayloadHash != hash) throw Error("REQUEST_ID_CONFLICT", "请求标识对应不同的操作");
            return Parse<ActivityClaimResponse>(previous.ResponseJson);
        }
        var config = await reader.GetAsync(ct);
        var profile = await Player(playerId, ct);
        var now = clock.GetUtcNow();
        var definitions = await Definitions(ct);
        var definition = definitions.SingleOrDefault(x => x.Id == id) ?? throw Error("ACTIVITY_DISABLED", "活动已下架");
        if (definition.DefinitionVersion != request.DefinitionVersion) throw Error("ACTIVITY_VERSION_CHANGED", "活动已更新，请刷新后重新确认");
        if (definition.Type == ActivityType.Notice || (definition.Type == ActivityType.Exchange) != exchange) throw Error("INVALID_ACTIVITY_OPERATION", "活动操作类型不匹配", 422);
        if (definition.ScheduleMode == ActivityScheduleMode.Timed)
        {
            if (now < definition.StartsAt) throw Error("ACTIVITY_NOT_STARTED", "活动尚未开始");
            if (now >= definition.EndsAt) throw Error("ACTIVITY_ENDED", "活动已结束");
        }
        var entry = definition.Entries.SingleOrDefault(x => x.Id == request.EntryId) ?? throw Error("INVALID_ACTIVITY_ENTRY", "活动领取项不存在", 422);
        var period = Period(entry.PeriodKind, now);
        if (request.PeriodKey != period) throw Error("PERIOD_CHANGED", "每日周期已变化，请刷新后重新确认");
        if (profile.Revision != request.ExpectedRevision) throw Error("PLAYER_DATA_CHANGED", "玩家数据已变化，请刷新");
        var progress = await db.Set<PlayerActivityProgress>().Where(x => x.PlayerId == playerId).ToListAsync(ct);
        if (!Eligible(definition, profile, progress)) throw Error("ACTIVITY_CONDITION_NOT_MET", "尚未满足参与条件");
        long value = progress.FirstOrDefault(x => x.ActivityId == id)?.Value ?? 0;
        if (definition.Type == ActivityType.SignIn && (value < entry.DayIndex || !definition.AllowCatchUpClaims && value != entry.DayIndex) || definition.Type == ActivityType.Milestone && value < entry.Threshold)
            throw Error("ACTIVITY_CONDITION_NOT_MET", "尚未解锁该奖励");
        var counter = await Counter(playerId, id, entry.Id, period, ct);
        var total = period == "all" ? counter : await Counter(playerId, id, entry.Id, "all", ct);
        if (counter.Count >= entry.LimitPerPeriod || entry.TotalLimit.HasValue && total.Count >= entry.TotalLimit) throw Error("LIMIT_REACHED", "已达到领取上限");
        if (profile.Gold < entry.CostGold) throw Error("INSUFFICIENT_GOLD", "金币不足", 422);
        var grants = entry.Rewards.Where(x => x.RewardType == ActivityRewardType.Card)
            .Select(x => new OwnedCard(config.ResolveCardId(x.RewardId), x.CardVariant, checked((int)x.Amount))).ToArray();
        var settlement = CardInventorySettlement.Grant(profile.OwnedCards, grants, CardEconomyConfiguration.From(config));
        long gold = entry.Rewards.Where(x => x.RewardType == ActivityRewardType.Gold).Sum(x => x.Amount);
        try { profile.Gold = checked(profile.Gold - entry.CostGold + gold); }
        catch (OverflowException) { throw Error("GOLD_OVERFLOW", "金币超出上限", 422); }
        profile.OwnedCards = settlement.Cards;
        profile.OwnedArtIds = profile.OwnedArtIds.Concat(entry.Rewards.Where(x => x.RewardType == ActivityRewardType.Card).Select(x => x.RewardId)).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        profile.Ur = CardInventorySettlement.AddUr(profile.Ur, settlement.UrGained);
        profile.Revision++;
        profile.UpdatedAt = now;
        counter.Count++;
        if (total != counter) total.Count++;
        db.Add(new PlayerActivityClaim { Id = Guid.NewGuid(), PlayerId = playerId, ActivityId = id, EntryId = entry.Id,
            PeriodKey = period, Ordinal = counter.Count, Version = definition.DefinitionVersion, RewardSnapshot = Encode(new { gold, settlement }), CreatedAt = now.ToString("O") });
        await db.SaveChangesAsync(ct);
        var p = await Progress(playerId, id, ct);
        p.Completed = definition.Entries.All(e => e.TotalLimit.HasValue && db.Set<PlayerActivityCounter>().Any(c => c.PlayerId == playerId && c.ActivityId == id && c.EntryId == e.Id && c.PeriodKey == "all" && c.Count >= e.TotalLimit.Value));
        p.Revision++;
        await db.SaveChangesAsync(ct);
        var response = new ActivityClaimResponse(PlayerService.ToResponse(profile), await Build(profile, now, ct), gold, settlement.UrGained, settlement.Results);
        db.Add(new ActivityOperation { PlayerId = playerId, RequestId = request.RequestId, PayloadHash = hash, ResponseJson = Encode(response), CreatedAt = now.ToString("O") });
        return response;
    }, ct);

    async Task<PlayerActivityCounter> Counter(Guid player, string activity, string entry, string period, CancellationToken ct)
    {
        var row = await db.Set<PlayerActivityCounter>().FindAsync(new object[] { player, activity, entry, period }, ct);
        if (row != null) return row;
        row = new PlayerActivityCounter { PlayerId = player, ActivityId = activity, EntryId = entry, PeriodKey = period };
        db.Add(row);
        return row;
    }

    public Task<ActivityListResponse> PopupAsync(Guid player, string id, ActivityPopupShownRequest request, CancellationToken ct) => Write(async () =>
    {
        await reader.GetAsync(ct);
        var profile = await Player(player, ct);
        var now = clock.GetUtcNow();
        var definition = (await Definitions(ct)).SingleOrDefault(x => x.Id == id) ?? throw Error("ACTIVITY_DISABLED", "活动已下架");
        if (definition.Popup.PolicyVersion != request.PolicyVersion) throw Error("ACTIVITY_VERSION_CHANGED", "提醒策略已更新");
        if ((definition.Popup.Frequency == "oncePerDay" ? Day(now) : "all") != request.PeriodKey) throw Error("PERIOD_CHANGED", "提醒周期已变化");
        if (definition.DisplayMode == ActivityDisplayMode.Page || definition.ScheduleMode == ActivityScheduleMode.Timed && (now < definition.StartsAt || now >= definition.EndsAt))
            throw Error("ACTIVITY_ENDED", "当前活动不可弹出");
        if (!await db.Set<PlayerActivityPopup>().AnyAsync(x => x.PlayerId == player && x.ActivityId == id && x.PolicyVersion == request.PolicyVersion && x.PeriodKey == request.PeriodKey, ct))
            db.Add(new PlayerActivityPopup { PlayerId = player, ActivityId = id, PolicyVersion = request.PolicyVersion, PeriodKey = request.PeriodKey, ShownAt = now.ToString("O") });
        await db.SaveChangesAsync(ct);
        return await Build(profile, now, ct);
    }, ct);

    // Called before the existing draw transaction commits. A rejected/rolled-back draw contributes nothing.
    public async Task RecordDrawAsync(PlayerProfile player, int pack, int count, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var progress = await db.Set<PlayerActivityProgress>().Where(x => x.PlayerId == player.UserId).ToListAsync(ct);
        foreach (var definition in (await Definitions(ct)).Where(x => x.Type == ActivityType.Milestone && x.PackIds.Contains(pack)))
        {
            if (definition.ScheduleMode == ActivityScheduleMode.Timed && (now < definition.StartsAt || now >= definition.EndsAt) || !Eligible(definition, player, progress)) continue;
            var row = await Progress(player.UserId, definition.Id, ct);
            row.Value = checked(row.Value + count);
            row.Revision++;
        }
    }

    public Task<List<ActivityDefinitionRecord>> AdminList(CancellationToken ct) => db.Set<ActivityDefinitionRecord>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
    public Task<List<ActivityGiftRecord>> Gifts(CancellationToken ct) => db.Set<ActivityGiftRecord>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct);
    public Task<List<ActivityPublishedVersion>> History(string id, CancellationToken ct) => db.Set<ActivityPublishedVersion>().AsNoTracking().Where(x => x.ActivityId == id).OrderByDescending(x => x.Version).ToListAsync(ct);
    public async Task<object> Translations(string target, CancellationToken ct)
    {
        var manifest = await content.LatestAsync(target, ct);
        var config = await content.ConfigAsync(target, manifest.configHash, ct);
        return new { target, configHash = manifest.configHash, entries = Table.TranslationRow.LoadBytes(config.TranslationTable).Select(x => new { x.Key, x.Chinese, x.English }),
            cards = config.AllCards.Select(x => x.CardId), packs = config.Catalog.CardPacks.Select(x => new { x.Id, x.Title }),
            resources = ResourceKeys(config) };
    }

    public Task<ActivityGiftRecord> SaveGift(ActivityGiftDefinition gift, long expectedVersion, CancellationToken ct) => Write(async () =>
    {
        ValidateId(gift.Id);
        var row = await db.Set<ActivityGiftRecord>().FindAsync(new object[] { gift.Id }, ct);
        if (row?.Frozen == true) throw Error("GIFT_FROZEN", "已发布礼包不可修改，请复制为新礼包");
        if ((row?.Revision ?? 0) != expectedVersion) throw Error("ACTIVITY_VERSION_CHANGED", "礼包已被其他管理员更新");
        if (row == null) { row = new ActivityGiftRecord { Id = gift.Id }; db.Add(row); }
        if (gift.NameKey.Length == 0 || gift.Rewards.Count == 0 || gift.Rewards.Any(x => x.Amount <= 0)) throw Error("INVALID_GIFT", "礼包名称或奖励无效", 422);
        row.DefinitionJson = Encode(gift); row.Revision++;
        return row;
    }, ct);

    public Task<ActivityDefinitionRecord> SaveDraft(string id, ActivityDraftRequest request, CancellationToken ct) => Write(async () =>
    {
        ValidateId(id);
        if (request.Definition.Id != id) throw Error("INVALID_ACTIVITY_ID", "活动 ID 不匹配", 422);
        LatestContentService.ValidateTarget(request.Target);
        var row = await db.Set<ActivityDefinitionRecord>().FindAsync(new object[] { id }, ct);
        if ((row?.Revision ?? 0) != request.ExpectedVersion) throw Error("ACTIVITY_VERSION_CHANGED", "草稿已被其他管理员更新");
        if (row == null) { row = new ActivityDefinitionRecord { Id = id }; db.Add(row); }
        if (row.ActiveVersion > 0 && row.Target != request.Target) throw Error("ACTIVITY_FROZEN", "已发布活动不能更换内容目标");
        row.Target = request.Target; row.ConfigHash = request.ConfigHash; row.DraftJson = Encode(request.Definition); row.Revision++;
        return row;
    }, ct);

    public Task<ActivityDefinitionRecord> Publish(string id, long expectedVersion, string actor, CancellationToken ct) => Write(async () =>
    {
        var row = await db.Set<ActivityDefinitionRecord>().SingleAsync(x => x.Id == id, ct);
        if (row.Revision != expectedVersion) throw Error("ACTIVITY_VERSION_CHANGED", "草稿版本已变化");
        var definition = Parse<ActivityDefinition>(row.DraftJson);
        var config = await content.ConfigAsync(row.Target, row.ConfigHash, ct);
        foreach (var entry in definition.Entries)
        {
            var gift = await db.Set<ActivityGiftRecord>().SingleOrDefaultAsync(x => x.Id == entry.GiftId, ct) ?? throw Error("INVALID_GIFT", "礼包不存在", 422);
            var data = Parse<ActivityGiftDefinition>(gift.DefinitionJson);
            if (definition.Type != ActivityType.Exchange) entry.NameKey = data.NameKey;
            entry.Rewards = data.Rewards;
            gift.Frozen = true;
        }
        await Validate(definition, config, row.Target, ct);
        if (row.ActiveVersion > 0)
        {
            var old = Parse<ActivityDefinition>((await db.Set<ActivityPublishedVersion>().SingleAsync(x => x.ActivityId == id && x.Version == row.ActiveVersion, ct)).DefinitionJson);
            if (definition.Type != old.Type) throw Error("ACTIVITY_FROZEN", "已发布活动不能更换玩法");
            if (await db.Set<PlayerActivityProgress>().AnyAsync(x => x.ActivityId == id, ct) || await db.Set<PlayerActivityClaim>().AnyAsync(x => x.ActivityId == id, ct))
            {
                string Identity(ActivityDefinition d) => Encode(new { d.Type, d.ScheduleMode, d.StartsAt, d.RequiredDays, d.AllowCatchUpClaims, d.MetricType, d.PackIds, entries = d.Entries.OrderBy(x => x.Id).Select(e => new { e.Id, e.GiftId, e.Rewards, e.CostGold, e.PeriodKind, e.LimitPerPeriod, e.TotalLimit, e.DayIndex, e.Threshold }) });
                if (Identity(old) != Identity(definition)) throw Error("ACTIVITY_FROZEN", "已有玩家记录，奖励、成本、周期和档位不可修改，请复制新活动");
            }
            if (definition.Popup.PolicyVersion < old.Popup.PolicyVersion) throw Error("INVALID_POPUP_POLICY", "提醒策略版本不可回退", 422);
        }
        definition.DefinitionVersion = ++row.ActiveVersion;
        row.PublishStatus = 1; row.Revision++;
        row.DraftJson = Encode(definition);
        db.Add(new ActivityPublishedVersion { ActivityId = id, Version = row.ActiveVersion, DefinitionJson = row.DraftJson, Actor = actor, PublishedAt = clock.GetUtcNow().ToString("O") });
        return row;
    }, ct);

    public Task<ActivityDefinitionRecord> Disable(string id, long expectedVersion, string actor, CancellationToken ct) => Write(async () =>
    {
        var row = await db.Set<ActivityDefinitionRecord>().SingleAsync(x => x.Id == id, ct);
        if (row.Revision != expectedVersion) throw Error("ACTIVITY_VERSION_CHANGED", "活动版本已变化");
        row.PublishStatus = 2; row.Revision++;
        var last = await db.Set<ActivityPublishedVersion>().SingleAsync(x => x.ActivityId == id && x.Version == row.ActiveVersion, ct);
        db.Add(new ActivityPublishedVersion { ActivityId = id, Version = ++row.ActiveVersion, DefinitionJson = last.DefinitionJson, Actor = actor, Action = "disable", PublishedAt = clock.GetUtcNow().ToString("O") });
        return row;
    }, ct);

    public Task<ActivityDefinitionRecord> Copy(string id, string newId, CancellationToken ct) => Write(async () =>
    {
        ValidateId(newId);
        if (await db.Set<ActivityDefinitionRecord>().AnyAsync(x => x.Id == newId, ct)) throw Error("ACTIVITY_ID_EXISTS", "活动 ID 已存在");
        var source = await db.Set<ActivityDefinitionRecord>().SingleAsync(x => x.Id == id, ct);
        var definition = Parse<ActivityDefinition>(source.DraftJson); definition.Id = newId; definition.DefinitionVersion = 0;
        var row = new ActivityDefinitionRecord { Id = newId, Target = source.Target, ConfigHash = source.ConfigHash, DraftJson = Encode(definition), Revision = 1 };
        db.Add(row); return row;
    }, ct);

    static void ValidateId(string id)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-zA-Z0-9_-]{1,96}$")) throw Error("INVALID_ACTIVITY_ID", "ID 须为 1-96 个字母数字下划线或连字符", 422);
    }
    static string[] ResourceKeys(PublishedGameConfig config) => config.Extra.TryGetValue("activity-resources", out var rows) ? rows.Select(x => (string)x["ResourceKey"]).ToArray() : [];
    async Task Validate(ActivityDefinition d, PublishedGameConfig config, string target, CancellationToken ct)
    {
        var translations = Table.TranslationRow.LoadBytes(config.TranslationTable).ToDictionary(x => x.Key);
        void Name(string key)
        {
            if (!translations.TryGetValue(key, out var text) || string.IsNullOrWhiteSpace(text.Chinese) || string.IsNullOrWhiteSpace(text.English) || text.Chinese.Contains('{') || text.English.Contains('{'))
                throw Error("INVALID_NAME_KEY", "名称须引用已发布且完整的中英文文案: " + key, 422);
        }
        void Resource(string key) { if (key.Length > 0 && !ResourceKeys(config).Contains(key)) throw Error("INVALID_ACTIVITY_RESOURCE", "活动图片未发布: " + key, 422); }
        Name(d.NameKey);
        if (d.DescriptionKey.Length > 0 && !translations.ContainsKey(d.DescriptionKey)) throw Error("INVALID_NAME_KEY", "说明文案未发布", 422);
        if (!Enum.IsDefined(d.Type) || !Enum.IsDefined(d.DisplayMode) || !Enum.IsDefined(d.ScheduleMode)) throw Error("INVALID_ACTIVITY", "活动类型或展示方式无效", 422);
        if (d.ScheduleMode == ActivityScheduleMode.Timed ? !d.StartsAt.HasValue || !d.EndsAt.HasValue || d.StartsAt >= d.EndsAt : d.StartsAt.HasValue || d.EndsAt.HasValue)
            throw Error("INVALID_ACTIVITY_TIME", "活动排期无效", 422);
        if (d.Popup.PolicyVersion < 1 || d.Popup.Trigger is not ("lobbyReady" or "rewardClaimable") || d.Popup.Frequency is not ("oncePerActivity" or "oncePerDay")) throw Error("INVALID_POPUP_POLICY", "弹窗规则无效", 422);
        if (d.Type != ActivityType.Notice && d.DisplayMode == ActivityDisplayMode.Popup) throw Error("INVALID_ACTIVITY_DISPLAY", "奖励活动必须保留页面领取入口", 422);
        Resource(d.BannerResourceKey); Resource(d.Notice.ImageResourceKey);
        if (d.Type == ActivityType.Notice)
        {
            if (d.Notice.Content.Length == 0 || d.Entries.Count > 0 || d.Notice.ActionKind is not ("close" or "activity")) throw Error("INVALID_NOTICE", "公告内容或操作无效", 422);
            if (d.Notice.ActionKind == "activity" && !await db.Set<ActivityDefinitionRecord>().AnyAsync(x => x.Id == d.Notice.ActionTarget && x.Target == target && x.PublishStatus == 1, ct))
                throw Error("INVALID_NOTICE", "公告跳转活动未发布", 422);
        }
        else if (d.Entries.Count == 0 || d.Entries.Select(x => x.Id).Distinct().Count() != d.Entries.Count) throw Error("INVALID_ACTIVITY_ENTRIES", "领取项缺失或 ID 重复", 422);
        foreach (var e in d.Entries)
        {
            ValidateId(e.Id); Name(e.NameKey);
            if (!Enum.IsDefined(e.PeriodKind) || e.LimitPerPeriod <= 0 || e.TotalLimit is <= 0 || e.Rewards.Count == 0 || (d.Type == ActivityType.Exchange ? e.CostGold <= 0 : e.CostGold != 0)) throw Error("INVALID_ACTIVITY_ENTRY", "领取次数或成本无效", 422);
            if (d.Type is ActivityType.SignIn or ActivityType.Milestone && (e.PeriodKind != ActivityPeriodKind.WholeActivity || e.LimitPerPeriod != 1 || e.TotalLimit != 1)) throw Error("INVALID_ACTIVITY_ENTRY", "档位必须全活动限领一次", 422);
            foreach (var reward in e.Rewards)
            {
                if (!Enum.IsDefined(reward.RewardType) || reward.Amount <= 0 || reward.RewardType == ActivityRewardType.Gold && reward.RewardId != "gold") throw Error("INVALID_REWARD", "奖励无效", 422);
                if (reward.RewardType == ActivityRewardType.Card && (reward.Amount > int.MaxValue || reward.CardVariant is < 0 or > 4 || !config.AllCards.Any(x => x.CardId == config.ResolveCardId(reward.RewardId)))) throw Error("INVALID_CARD_GRANT", "卡牌奖励未发布或版本无效", 422);
            }
            try { _ = e.Rewards.Where(x => x.RewardType == ActivityRewardType.Gold).Sum(x => x.Amount); }
            catch (OverflowException) { throw Error("GOLD_OVERFLOW", "奖励金币超出上限", 422); }
        }
        if (d.Type == ActivityType.SignIn && (d.RequiredDays < 1 || d.Entries.Count != d.RequiredDays || !d.Entries.Select(x => x.DayIndex).Order().SequenceEqual(Enumerable.Range(1, d.RequiredDays)))) throw Error("INVALID_SIGN_IN", "签到档位须从第 1 天连续配置", 422);
        if (d.Type == ActivityType.Milestone && (d.MetricType != "gachaDrawCount" || d.PackIds.Count == 0 || d.PackIds.Any(x => !config.Catalog.CardPacks.Any(p => p.Id == x)) || d.Entries.Any(x => x.Threshold <= 0) || d.Entries.Select(x => x.Threshold).Distinct().Count() != d.Entries.Count)) throw Error("INVALID_MILESTONE", "抽卡里程碑配置无效", 422);
        var all = (await db.Set<ActivityDefinitionRecord>().Where(x => x.PublishStatus == 1).ToListAsync(ct)).ToDictionary(x => x.Id);
        foreach (var c in d.OpenConditions.AllOf)
        {
            if (c.Type == "ownedCardKindsAtLeast" && c.Value >= 0 || c.Type is "playerCreatedBefore" or "playerCreatedAfter" && c.Time.HasValue) continue;
            if (c.Type != "activityCompleted" || c.ActivityId == d.Id || !all.TryGetValue(c.ActivityId, out var dependent) || dependent.Target != target) throw Error("INVALID_ACTIVITY_CONDITION", "活动条件或依赖无效", 422);
            var version = await db.Set<ActivityPublishedVersion>().SingleAsync(x => x.ActivityId == dependent.Id && x.Version == dependent.ActiveVersion, ct);
            var child = Parse<ActivityDefinition>(version.DefinitionJson);
            if (child.Type == ActivityType.Notice || child.Entries.Any(x => !x.TotalLimit.HasValue)) throw Error("INVALID_ACTIVITY_CONDITION", "前置活动必须可永久完成", 422);
            async Task CheckCycle(ActivityDefinition node, HashSet<string> seen)
            {
                if (!seen.Add(node.Id)) throw Error("INVALID_ACTIVITY_CONDITION", "活动依赖存在循环", 422);
                foreach (var condition in node.OpenConditions.AllOf.Where(x => x.Type == "activityCompleted"))
                {
                    if (condition.ActivityId == d.Id) throw Error("INVALID_ACTIVITY_CONDITION", "活动依赖存在循环", 422);
                    if (all.TryGetValue(condition.ActivityId, out var next))
                    { var v = await db.Set<ActivityPublishedVersion>().SingleAsync(x => x.ActivityId == next.Id && x.Version == next.ActiveVersion, ct); await CheckCycle(Parse<ActivityDefinition>(v.DefinitionJson), new HashSet<string>(seen)); }
                }
            }
            await CheckCycle(child, new HashSet<string> { d.Id });
        }
    }
}
