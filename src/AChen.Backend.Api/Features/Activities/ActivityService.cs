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

public sealed record ActivityClaimResponse(PlayerResponse Player, ActivityIndexResponse Activities, long GoldGained, long UrGained, IReadOnlyList<CardGrantResult> Cards);
public sealed class ActivityService(AppDbContext db, PublishedConfigReader reader,
    IPlayerRepository players, ActivityTimeProvider clock, TimeProvider systemClock, ActivityGate gate, ActivityConfigurationStore configuration)
{
    internal static readonly JsonSerializerOptions Json = LatestContentService.Json;
    static T Parse<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)!;
    static string Encode<T>(T value) => JsonSerializer.Serialize(value, Json);
    static ApiException Error(string code, string text, int status = 409) => new(status, code, text);
    public static string Day(DateTimeOffset now) => now.ToOffset(TimeSpan.FromHours(8)).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    static string Period(ActivityPeriodKind kind, DateTimeOffset now) => kind == ActivityPeriodKind.Daily ? Day(now) : "all";
    async Task<PlayerProfile> Player(Guid id, CancellationToken ct) => await players.GetOrCreateAsync(id, systemClock.GetUtcNow(), ct)
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

    public async Task<ActivityIndexResponse> ListAsync(Guid id, bool visit, CancellationToken ct)
    {
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
            return await configuration.Index(await Build(profile, now, ct), ct);
        }, ct);
        await gate.Mutex.WaitAsync(ct);
        try { return await configuration.Index(await Build(await Player(id, ct), clock.GetUtcNow(), ct), ct); }
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

    Task<List<ActivityDefinition>> Definitions(CancellationToken ct) => configuration.Definitions(ct);

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
        var cardRewards = entry.Rewards.Where(x => x.RewardType == ActivityRewardType.Card).ToArray();
        var settlement = new InventorySettlement(profile.OwnedCards.ToList(), [], 0);
        if (cardRewards.Length > 0)
        {
            var config = await reader.GetAsync(ct);
            var grants = cardRewards.Select(x => new OwnedCard(config.ResolveCardId(x.RewardId), x.CardVariant, checked((int)x.Amount))).ToArray();
            settlement = CardInventorySettlement.Grant(profile.OwnedCards, grants, CardEconomyConfiguration.From(config));
        }
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
        var response = new ActivityClaimResponse(PlayerService.ToResponse(profile), await configuration.Index(await Build(profile, now, ct), ct), gold, settlement.UrGained, settlement.Results);
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

    public Task<ActivityIndexResponse> PopupAsync(Guid player, string id, ActivityPopupShownRequest request, CancellationToken ct) => Write(async () =>
    {
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
        return await configuration.Index(await Build(profile, now, ct), ct);
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

}
