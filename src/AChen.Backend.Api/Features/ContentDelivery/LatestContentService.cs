using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using AChen.Backend.Api.Data;
using AChen.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AChen.Backend.Api.Features.ContentDelivery;

public sealed class CurrentContent
{
    public string Target { get; set; } = "";
    public string ContentId { get; set; } = "";
    public string ManifestJson { get; set; } = "";
}

// 发布与打开文件共用锁; 已打开的流允许删除, 因而不会混读后续发布的文件.
public sealed class ContentGate { public SemaphoreSlim Mutex { get; } = new(1, 1); }

public sealed class LatestContentService(AppDbContext db, IOptions<ContentDeliveryOptions> options,
    IHostEnvironment environment, ContentGate gate, ILogger<LatestContentService> logger)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { IncludeFields = true };
    readonly string root = Path.GetFullPath(Path.IsPathRooted(options.Value.StorageRoot)
        ? options.Value.StorageRoot : Path.Combine(environment.ContentRootPath, options.Value.StorageRoot));

    static ContentDeliveryException Error(int status, string code, string message) => new(status, code, message);
    public static void ValidateTarget(string target)
    {
        if (target is not ("Editor" or "Android" or "StandaloneWindows64" or "iOS"))
            throw Error(400, "INVALID_TARGET", "不支持的内容目标");
    }
    string Scoped(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.Contains('\\') || relative.Contains(':') ||
            relative.Split('/').Any(x => x is "" or "." or "..")) throw Error(422, "INVALID_CONTENT_PACKAGE", "内容路径无效");
        var path = Path.GetFullPath(Path.Combine(root, relative));
        if (!path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw Error(422, "INVALID_CONTENT_PACKAGE", "内容路径超出目录");
        for (string? directory = path; directory != null && directory.Length >= root.Length; directory = Path.GetDirectoryName(directory))
            if ((Directory.Exists(directory) || File.Exists(directory)) && (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("内容路径包含链接, 拒绝操作: " + relative);
        return path;
    }
    void Delete(string relative)
    {
        var path = Scoped(relative);
        if (File.Exists(path)) throw new IOException("内容清理路径不是目录: " + relative);
        if (!Directory.Exists(path)) return;
        // 不跟随磁盘上的链接清理其他目录.
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0 ||
            Directory.EnumerateFileSystemEntries(path, "*", SearchOption.AllDirectories)
                .Any(x => (File.GetAttributes(x) & FileAttributes.ReparsePoint) != 0))
            throw new IOException("内容目录包含链接, 拒绝清理: " + relative);
        Directory.Delete(path, true);
    }
    static string ContentDirectory(DevelopmentManifest manifest) =>
        manifest.platform != "Editor" && DevelopmentProtocol.ValidContentVersion(manifest.contentVersion)
            ? "current/" + manifest.platform + "/" + DevelopmentProtocol.VersionFolder(manifest.contentVersion, manifest.platform)
            : "current/" + Guid.Parse(manifest.contentId).ToString("D");

    async Task RemovePlatformAsync(string target, CancellationToken ct)
    {
        // 只按数据库所属平台定位旧 GUID 目录; 不扫描或删除其他平台的内容.
        var row = await db.Set<CurrentContent>().SingleOrDefaultAsync(x => x.Target == target, ct);
        Delete("current/" + target);
        if (row != null)
        {
            Delete("current/" + Guid.Parse(row.ContentId).ToString("D"));
            db.Remove(row);
            await db.SaveChangesAsync(ct);
        }
    }
    public async Task CleanupAsync(CancellationToken ct)
    {
        await gate.Mutex.WaitAsync(ct);
        try
        {
            Directory.CreateDirectory(root);
            var rows = await db.Set<CurrentContent>().AsNoTracking().ToListAsync(ct);
            var keep = rows.Select(x => ContentDirectory(JsonSerializer.Deserialize<DevelopmentManifest>(x.ManifestJson, Json)!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var parent in new[] { "staging", "releases", "current" })
            {
                var path = Scoped(parent);
                if (!Directory.Exists(path)) continue;
                foreach (var child in Directory.EnumerateDirectories(path))
                {
                    var name = Path.GetFileName(child);
                    if (Guid.TryParse(name, out _) && (parent != "current" || !keep.Contains(parent + "/" + name)))
                        TryDelete(parent + "/" + name);
                    else if (parent == "current" && name is "Android" or "StandaloneWindows64")
                    {
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                        foreach (var version in Directory.EnumerateDirectories(child))
                        {
                            var relative = parent + "/" + name + "/" + Path.GetFileName(version);
                            if (!keep.Contains(relative)) TryDelete(relative);
                        }
                    }
                }
            }
        }
        finally { gate.Mutex.Release(); }
    }
    void TryDelete(string relative)
    {
        try { Delete(relative); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { logger.LogWarning(ex, "内容清理延后. Path={Path}", relative); }
    }
    public async Task<DevelopmentManifest> LatestAsync(string target, CancellationToken ct)
    {
        ValidateTarget(target);
        var row = await db.Set<CurrentContent>().AsNoTracking().SingleOrDefaultAsync(x => x.Target == target, ct)
            ?? throw Error(404, "CONTENT_NOT_READY", "尚未发布此平台内容");
        var manifest = JsonSerializer.Deserialize<DevelopmentManifest>(row.ManifestJson, Json)!;
        manifest.serverTime = DateTimeOffset.UtcNow.ToString("O");
        return manifest;
    }
    public Task<List<CurrentContent>> StatusAsync(CancellationToken ct) => db.Set<CurrentContent>().AsNoTracking().ToListAsync(ct);

    public async Task<DevelopmentManifest> PublishAsync(string target, Stream input, string archiveHash, CancellationToken ct)
    {
        ValidateTarget(target);
        if (target is not ("Editor" or "Android" or "StandaloneWindows64"))
            throw Error(400, "INVALID_TARGET", "发布仅支持 Android 和 Windows x64");
        if (!ContentDeliveryValidation.IsSha256(archiveHash)) throw Error(400, "INVALID_HASH", "缺少归档 SHA256");
        await gate.Mutex.WaitAsync(ct);
        var id = Guid.NewGuid().ToString("D");
        var stage = "staging/" + id;
        string? final = null;
        var committed = false;
        try
        {
            // Player 发布明确采用先删旧版语义. 删除失败时不会读取上传流.
            if (target != "Editor") await RemovePlatformAsync(target, ct);
            Directory.CreateDirectory(Scoped(stage));
            var zipPath = Scoped(stage + "/upload.zip");
            await using (var output = File.Create(zipPath))
            {
                var buffer = new byte[131072]; long total = 0; int count;
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                while ((count = await input.ReadAsync(buffer, ct)) > 0)
                {
                    total += count;
                    if (total > options.Value.MaxArchiveBytes) throw Error(413, "CONTENT_ARCHIVE_TOO_LARGE", "上传文件过大");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), ct);
                }
                if (!Convert.ToHexString(hash.GetHashAndReset()).Equals(archiveHash, StringComparison.OrdinalIgnoreCase))
                    throw Error(422, "CONTENT_ARCHIVE_HASH_MISMATCH", "上传文件校验失败");
            }
            DevelopmentManifest manifest;
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                if (zip.Entries.Count > options.Value.MaxFileCount + 1 || zip.Entries.Sum(x => x.Length) > options.Value.MaxExpandedBytes)
                    throw Error(413, "CONTENT_ARCHIVE_TOO_LARGE", "归档解压大小或文件数量超限");
                var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
                foreach (var entry in zip.Entries)
                {
                    Scoped(stage + "/files/" + entry.FullName);
                    if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000 || !entries.TryAdd(entry.FullName, entry))
                        throw Error(422, "INVALID_CONTENT_PACKAGE", "归档包含链接或重复路径");
                }
                if (!entries.TryGetValue("manifest.json", out var manifestEntry) || manifestEntry.Length > 1024 * 1024)
                    throw Error(422, "INVALID_CONTENT_PACKAGE", "缺少有效 manifest.json");
                await using (var stream = manifestEntry.Open())
                    manifest = await JsonSerializer.DeserializeAsync<DevelopmentManifest>(stream, Json, ct)
                        ?? throw new FormatException("清单为空");
                if (manifest.schemaVersion != DevelopmentProtocol.Version || manifest.platform != target)
                    throw Error(422, "CONTENT_PROTOCOL_MISMATCH", "发布工具与后端协议或目标不一致");
                ConfigArtifacts.Validate(manifest.configs, "");
                if (manifest.files == null || manifest.files.Length != entries.Count - 1 ||
                    manifest.files.Select(x => x.path).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.files.Length)
                    throw new FormatException("文件清单不完整或重复");
                Directory.CreateDirectory(Scoped(stage + "/files"));
                foreach (var file in manifest.files)
                {
                    if (file.path == "manifest.json" || !entries.TryGetValue(file.path, out var entry) || entry.Length != file.size ||
                        !ContentDeliveryValidation.IsSha256(file.sha256)) throw new FormatException("文件元数据无效: " + file.path);
                    var path = Scoped(stage + "/files/" + file.path);
                    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                    await using (var source = entry.Open())
                    await using (var output = File.Create(path)) await source.CopyToAsync(output, ct);
                    await using var check = File.OpenRead(path);
                    if (!Convert.ToHexString(await SHA256.HashDataAsync(check, ct)).Equals(file.sha256, StringComparison.OrdinalIgnoreCase))
                        throw new FormatException("文件哈希不符: " + file.path);
                }
                var configs = new Dictionary<string, byte[]>();
                foreach (var config in manifest.configs)
                    configs.Add(config.category, await File.ReadAllBytesAsync(Scoped(stage + "/files/" + config.path), ct));
                ConfigArtifacts.Verify(configs, manifest.configs);
                GameConfigTables.Assemble(configs);
                if (manifest.configHash != DevelopmentProtocol.ConfigHash(manifest.configs)) throw new FormatException("配置哈希不符");
                if (target != "Editor")
                {
                    if (!DevelopmentProtocol.ValidContentVersion(manifest.contentVersion) || manifest.hotUpdatePath != DevelopmentProtocol.HotUpdatePath ||
                        !manifest.files.Any(x => x.path == manifest.hotUpdatePath) ||
                        !manifest.files.Any(x => x.path == manifest.hotUpdatePath + ".sha256") ||
                        !manifest.files.Any(x => x.path == manifest.catalogPath && x.path.StartsWith("Addressables/") && x.path.EndsWith(".bin")) ||
                        !manifest.files.Any(x => x.path == manifest.catalogHashPath && x.path.StartsWith("Addressables/") && x.path.EndsWith(".hash")) ||
                        !manifest.files.Any(x => x.path.StartsWith("Addressables/") && x.path.EndsWith(".bundle")))
                        throw new FormatException("客户端内容缺少版本号、DLL 或资源目录");
                    var dll = manifest.files.Single(x => x.path == manifest.hotUpdatePath);
                    var recordedHash = (await File.ReadAllTextAsync(Scoped(stage + "/files/" + manifest.hotUpdatePath + ".sha256"), ct)).Trim();
                    if (!dll.sha256.Equals(recordedHash, StringComparison.OrdinalIgnoreCase))
                        throw new FormatException("DLL 哈希记录不一致");
                }
            }
            manifest.contentId = id;
            manifest.serverTime = DateTimeOffset.UtcNow.ToString("O");
            var manifestJson = JsonSerializer.Serialize(manifest, Json);
            await File.WriteAllTextAsync(Scoped(stage + "/files/manifest.json"), manifestJson, ct);
            final = ContentDirectory(manifest);
            Directory.CreateDirectory(Path.GetDirectoryName(Scoped(final))!);
            Directory.Move(Scoped(stage + "/files"), Scoped(final));
            var row = await db.Set<CurrentContent>().SingleOrDefaultAsync(x => x.Target == target, ct);
            var old = row?.ContentId;
            if (row == null) { row = new CurrentContent { Target = target }; db.Add(row); }
            row.ContentId = id;
            row.ManifestJson = manifestJson;
            await db.SaveChangesAsync(ct);
            committed = true;
            if (old != null) TryDelete("current/" + old);
            logger.LogInformation("内容替换完成. Target={Target}; Content={Content}; Result=Success", target, id);
            return manifest;
        }
        catch (Exception ex) when (ex is FormatException or JsonException or InvalidDataException or KeyNotFoundException or NullReferenceException)
        {
            logger.LogWarning(ex, "内容发布校验失败. Target={Target}; Content={Content}", target, id);
            throw Error(422, "INVALID_CONTENT_PACKAGE", "内容包无效: " + ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "内容发布未完成. Target={Target}; Content={Content}; Committed={Committed}", target, id, committed);
            throw;
        }
        finally
        {
            try { TryDelete(stage); if (!committed && final != null) TryDelete(final); }
            finally { gate.Mutex.Release(); }
        }
    }

    public async Task<Stream> OpenAsync(string target, string id, string relative, CancellationToken ct)
    {
        await gate.Mutex.WaitAsync(ct);
        try
        {
            var manifest = await LatestAsync(target, ct);
            if (manifest.contentId != id) throw Error(409, "CONTENT_CHANGED", "内容已更新, 请重启获取最新内容");
            if (!manifest.files.Any(x => x.path == relative)) throw Error(404, "CONTENT_FILE_NOT_FOUND", "内容文件不存在");
            return new FileStream(Scoped(ContentDirectory(manifest) + "/" + relative), FileMode.Open, FileAccess.Read,
                FileShare.Read | FileShare.Delete, 131072, FileOptions.Asynchronous);
        }
        finally { gate.Mutex.Release(); }
    }
    public async Task<PublishedGameConfig> ConfigAsync(string target, string expectedHash, CancellationToken ct) =>
        (await ReadConfigAsync(target, expectedHash, ct)).Data;
    public async Task<(PublishedGameConfig Data, Guid ContentId)> ReadConfigAsync(string target, string expectedHash, CancellationToken ct)
    {
        await gate.Mutex.WaitAsync(ct);
        try
        {
            var manifest = await LatestAsync(target, ct);
            if (manifest.configHash != expectedHash) throw Error(409, "CONTENT_CHANGED", "配置已更新, 请重启客户端");
            var files = new Dictionary<string, byte[]>();
            foreach (var config in manifest.configs)
                files.Add(config.category, await File.ReadAllBytesAsync(Scoped(ContentDirectory(manifest) + "/" + config.path), ct));
            ConfigArtifacts.Verify(files, manifest.configs);
            return (GameConfigTables.Assemble(files), Guid.Parse(manifest.contentId));
        }
        finally { gate.Mutex.Release(); }
    }
}
