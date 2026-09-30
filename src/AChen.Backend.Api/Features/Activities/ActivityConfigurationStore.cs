using System.IO.Compression;
using System.Text.Json;
using AChen.Configuration;
using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AChen.Backend.Api.Features.Activities;

public sealed class ActivityConfigurationStore(AppDbContext db, ActivityGate gate,
    IOptions<ContentDeliveryOptions> options, IHostEnvironment environment, TimeProvider clock)
{
    static readonly JsonSerializerOptions Json = LatestContentService.Json;
    readonly string root = Path.GetFullPath(Path.Combine(Path.IsPathRooted(options.Value.StorageRoot)
        ? options.Value.StorageRoot : Path.Combine(environment.ContentRootPath, options.Value.StorageRoot), "activities"));
    static string Encode<T>(T data) => JsonSerializer.Serialize(data, Json);
    static T Parse<T>(string data) => JsonSerializer.Deserialize<T>(data, Json)!;
    static ApiException Invalid(string text) => new(422, "INVALID_ACTIVITY_PACKAGE", text);
    string FilePath(string release, string table)
    {
        if (!Guid.TryParseExact(release, "D", out _) || !ActivityCsvConfiguration.ValidName(table)) throw Invalid("版本或表名无效");
        var path = Path.Combine(root, release, table + ".bytes");
        for (var dir = Path.GetDirectoryName(path); dir != null && dir.Length >= root.Length; dir = Path.GetDirectoryName(dir))
            if (Directory.Exists(dir) && (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) throw Invalid("活动文件目录包含链接");
        return path;
    }
    public async Task<ActivityReleaseRecord?> Current(CancellationToken ct)
    {
        var current = await db.Set<CurrentActivityRelease>().AsNoTracking().SingleOrDefaultAsync(ct);
        return current == null ? null : await db.Set<ActivityReleaseRecord>().AsNoTracking().SingleAsync(x => x.ReleaseId == current.ReleaseId, ct);
    }
    public async Task<List<ActivityDefinition>> Definitions(CancellationToken ct)
    {
        var current = await Current(ct);
        if (current == null) return [];
        var masters = Parse<ActivityMasterRow[]>(current.MasterJson);
        var records = await db.Set<ActivityDefinitionRecord>().AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        var definitions = new List<ActivityDefinition>();
        foreach (var row in masters.Where(x => x.IsEnabled))
            definitions.Add(ActivityCsvConfiguration.Detail(row, await File.ReadAllBytesAsync(FilePath(current.ReleaseId, row.DetailTable), ct), records[row.ActivityId].ActiveVersion));
        return definitions;
    }
    public async Task<ActivityIndexResponse> Index(ActivityListResponse states, CancellationToken ct)
    {
        var current = await Current(ct);
        var index = new ActivityIndexResponse { DefinitionsRevision = current?.Revision ?? 0, ReleaseId = current?.ReleaseId ?? "",
            ServerTime = states.ServerTime, ServerDay = states.ServerDay, NextResetAt = states.NextResetAt, PlayerStateRevision = states.PlayerStateRevision };
        if (current == null) return index;
        var records = await db.Set<ActivityDefinitionRecord>().AsNoTracking().ToDictionaryAsync(x => x.Id, ct);
        foreach (var master in Parse<ActivityMasterRow[]>(current.MasterJson))
        {
            var state = states.Activities.SingleOrDefault(x => x.Definition.Id == master.ActivityId);
            index.Activities.Add(new ActivityIndexItem { Master = master, DefinitionVersion = records[master.ActivityId].ActiveVersion,
                Status = !master.IsEnabled ? "disabled" : master.IsOpen(states.ServerTime) ? "running" : states.ServerTime < master.StartsAt ? "upcoming" : "ended",
                Eligible = state?.Eligible ?? false, ShouldShow = state?.Definition.Popup.ShouldShow ?? false,
                LockedReasonCode = state?.LockedReasonCode ?? "", LockedCondition = state?.LockedCondition ?? new ActivityCondition(),
                PlayerState = state?.PlayerState ?? new ActivityPlayerState() });
        }
        index.Files = Parse<ActivityPackageManifest>(current.ManifestJson).Files.Select(x => new ActivityFileInfo
        { Table = x.Table, Size = x.Size, Sha256 = x.Sha256, Url = "/api/activities/files/" + current.ReleaseId + "/" + x.Table + ".bytes" }).ToList();
        return index;
    }
    public async Task<object> Admin(CancellationToken ct) => new
    {
        current = await Current(ct),
        activities = await db.Set<ActivityDefinitionRecord>().AsNoTracking().OrderBy(x => x.Id).ToListAsync(ct)
    };
    public Task<List<ActivityReleaseRecord>> History(CancellationToken ct) => db.Set<ActivityReleaseRecord>().AsNoTracking().OrderByDescending(x => x.Revision).ToListAsync(ct);
    public async Task<(string Path, ActivityFileInfo Info)> Download(string release, string table, CancellationToken ct)
    {
        var record = await db.Set<ActivityReleaseRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ReleaseId == release, ct)
            ?? throw new ApiException(404, "ACTIVITY_FILE_NOT_FOUND", "活动发布版本不存在");
        var info = Parse<ActivityPackageManifest>(record.ManifestJson).Files.SingleOrDefault(x => x.Table == table)
            ?? throw new ApiException(404, "ACTIVITY_FILE_NOT_FOUND", "活动版本未包含该表");
        return (FilePath(release, table), info);
    }
    public async Task<ActivityReleaseRecord> Publish(Stream input, string actor, CancellationToken ct)
    {
        // 限制实际解压长度，不能只相信 ZIP 声明的大小。
        using var package = new MemoryStream();
        byte[] buffer = new byte[65536]; int count;
        while ((count = await input.ReadAsync(buffer, ct)) > 0)
        { if (package.Length + count > 32 * 1024 * 1024) throw Invalid("活动包超过 32 MiB"); await package.WriteAsync(buffer.AsMemory(0, count), ct); }
        package.Position = 0;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        ActivityPackageManifest manifest;
        try
        {
            using var zip = new ZipArchive(package, ZipArchiveMode.Read, true);
            var entry = zip.Entries.Single(x => x.FullName == "manifest.json");
            using var text = new StreamReader(entry.Open());
            if (entry.Length > 65536) throw Invalid("清单过大");
            manifest = Parse<ActivityPackageManifest>(await text.ReadToEndAsync(ct));
            if (manifest.SchemaVersion != 2 || !Guid.TryParseExact(manifest.ReleaseId, "D", out _) ||
                manifest.Files.Count > 1000 || zip.Entries.Count != manifest.Files.Count + 1 ||
                manifest.Files.Select(x => x.Table).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Files.Count)
                throw Invalid("清单协议、版本 ID 或文件集合无效");
            long total = 0;
            foreach (var file in manifest.Files)
            {
                if (!ActivityCsvConfiguration.ValidName(file.Table) || file.Size <= 0 || file.Size > 8 * 1024 * 1024 || file.Sha256.Length != 64) throw Invalid(file.Table + ".bytes: 大小或哈希无效");
                var source = zip.Entries.Single(x => x.FullName == file.Table + ".bytes");
                if (source.Length != file.Size || (total += source.Length) > 32 * 1024 * 1024) throw Invalid(file.Table + ".bytes: 大小不匹配");
                using var stream = source.Open(); using var bytes = new MemoryStream();
                while ((count = await stream.ReadAsync(buffer, ct)) > 0)
                { if (bytes.Length + count > file.Size) throw Invalid(file.Table + ".bytes: 解压大小不匹配"); await bytes.WriteAsync(buffer.AsMemory(0, count), ct); }
                var data = bytes.ToArray();
                if (data.LongLength != file.Size || ActivityCsvConfiguration.Hash(data) != file.Sha256) throw Invalid(file.Table + ".bytes: SHA-256 不匹配");
                files.Add(file.Table, data);
            }
        }
        catch (ApiException) { throw; }
        catch (Exception e) when (e is not OperationCanceledException) { throw Invalid("活动包解析失败: " + e.Message); }
        Dictionary<string, ActivityDefinition> definitions;
        ActivityMasterRow[] masters;
        try
        {
            definitions = ActivityCsvConfiguration.Package(files); masters = ActivityCsvConfiguration.Master(files["activities"]);
        }
        catch (FormatException e) { throw Invalid(e.Message); }
        await gate.Mutex.WaitAsync(ct);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            var current = await db.Set<CurrentActivityRelease>().SingleOrDefaultAsync(ct);
            if ((current?.Revision ?? 0) != manifest.ExpectedRevision) throw new ApiException(409, "ACTIVITY_VERSION_CHANGED", "活动版本已变化，请重新获取当前发布版本");
            if (await db.Set<ActivityReleaseRecord>().AnyAsync(x => x.ReleaseId == manifest.ReleaseId, ct)) throw Invalid("ReleaseId 已使用，请为新发布生成新的版本 ID");
            var rows = await db.Set<ActivityDefinitionRecord>().ToDictionaryAsync(x => x.Id, ct);
            var published = clock.GetUtcNow().ToString("O");
            foreach (var master in masters)
            {
                var d = definitions[master.ActivityId];
                string identity = ActivityCsvConfiguration.Hash(System.Text.Encoding.UTF8.GetBytes(ActivityCsvConfiguration.Identity(d)));
                string json = Encode(master), hash = manifest.Files.Single(x => x.Table == master.DetailTable).Sha256;
                if (!rows.TryGetValue(master.ActivityId, out var row))
                { row = new ActivityDefinitionRecord { Id = master.ActivityId, IdentityHash = identity }; db.Add(row); }
                else
                {
                    if (row.IdentityHash != identity) throw Invalid(master.DetailTable + ".csv / Type,EntryId,DayIndex,Threshold,PeriodKind,LimitPerPeriod,TotalLimit,PackIds,AllowCatchUpClaims: 首次发布后固定档位规则不可变更");
                    if (master.PopupPolicyVersion < Parse<ActivityMasterRow>(row.MasterJson).PopupPolicyVersion) throw Invalid("activities.csv / PopupPolicyVersion: 策略版本不可回退");
                }
                if (row.MasterJson != json || row.DetailHash != hash)
                {
                    row.ActiveVersion++; row.Revision++; row.MasterJson = json; row.DetailHash = hash;
                    db.Add(new ActivityPublishedVersion { ActivityId = row.Id, Version = row.ActiveVersion, MasterJson = json, DetailHash = hash,
                        ReleaseId = manifest.ReleaseId, Actor = actor, PublishedAt = published });
                }
            }
            // 完整校验后先落不可变文件，再在一个事务里切换数据库指针。
            foreach (var file in files)
            {
                var path = FilePath(manifest.ReleaseId, file.Key); Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                await File.WriteAllBytesAsync(path, file.Value, ct);
            }
            current ??= new CurrentActivityRelease();
            if (current.Revision == 0) db.Add(current);
            current.Revision++; current.ReleaseId = manifest.ReleaseId;
            var release = new ActivityReleaseRecord { ReleaseId = manifest.ReleaseId, Revision = current.Revision, ManifestJson = Encode(manifest),
                MasterJson = Encode(masters), Actor = actor, PublishedAt = published };
            db.Add(release); await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct);
            return release;
        }
        finally { gate.Mutex.Release(); }
    }
}
