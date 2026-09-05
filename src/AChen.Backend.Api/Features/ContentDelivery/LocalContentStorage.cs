using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace AChen.Backend.Api.Features.ContentDelivery;

public sealed class LocalContentStorage : IContentStorage
{
    private const int BufferSize = 128 * 1024;
    private const long MaxManifestBytes = 1024 * 1024;
    private readonly ContentDeliveryOptions options;
    private readonly string root;
    private readonly string stagingRoot;
    private readonly string releasesRoot;
    private readonly TimeProvider timeProvider;

    public LocalContentStorage(
        IOptions<ContentDeliveryOptions> options,
        IHostEnvironment environment,
        TimeProvider timeProvider)
    {
        this.options = options.Value;
        this.timeProvider = timeProvider;
        root = Path.GetFullPath(Path.IsPathRooted(this.options.StorageRoot)
            ? this.options.StorageRoot
            : Path.Combine(environment.ContentRootPath, this.options.StorageRoot));
        stagingRoot = Path.Combine(root, "staging");
        releasesRoot = Path.Combine(root, "releases");
        Directory.CreateDirectory(stagingRoot);
        Directory.CreateDirectory(releasesRoot);
    }

    public async Task<ValidatedContentPackage> StoreReleaseAsync(
        ContentRelease release,
        Stream archive,
        long? contentLength,
        string expectedArchiveSha256,
        CancellationToken cancellationToken)
    {
        if (contentLength > options.MaxArchiveBytes)
        {
            throw Error(413, "CONTENT_ARCHIVE_TOO_LARGE", "内容归档文件超过上传大小限制");
        }

        var releaseStagingRoot = GetScopedPath(stagingRoot, release.Id);
        var archivePath = Path.Combine(releaseStagingRoot, "release.zip");
        var extractRoot = Path.Combine(releaseStagingRoot, "extracted");
        var finalRoot = GetScopedPath(releasesRoot, release.Id);

        DeleteDirectoryIfPresent(releaseStagingRoot);
        Directory.CreateDirectory(releaseStagingRoot);

        try
        {
            var staged = await WriteArchiveAsync(
                archive,
                archivePath,
                expectedArchiveSha256,
                cancellationToken);
            Directory.CreateDirectory(extractRoot);
            var validated = await ValidateAndExtractAsync(
                release,
                archivePath,
                extractRoot,
                staged.Hash,
                staged.Length,
                cancellationToken);

            DeleteDirectoryIfPresent(finalRoot);
            Directory.Move(extractRoot, finalRoot);
            DeleteDirectoryIfPresent(releaseStagingRoot);
            return validated;
        }
        catch
        {
            DeleteDirectoryIfPresent(releaseStagingRoot);
            throw;
        }
    }

    public Task<ContentFileRead?> OpenReadAsync(
        Guid releaseId,
        string relativePath,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var releaseRoot = GetScopedPath(releasesRoot, releaseId);
        var normalized = NormalizeRelativePath(relativePath);
        var path = ResolveWithin(releaseRoot, normalized);
        if (!File.Exists(path))
        {
            return Task.FromResult<ContentFileRead?>(null);
        }

        var info = new FileInfo(path);
        Stream stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Task.FromResult<ContentFileRead?>(new ContentFileRead(
            stream,
            info.Length,
            info.LastWriteTimeUtc));
    }

    public Task DeleteReleaseAsync(Guid releaseId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteDirectoryIfPresent(GetScopedPath(releasesRoot, releaseId));
        return Task.CompletedTask;
    }

    public Task DeleteStagingAsync(Guid releaseId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        DeleteDirectoryIfPresent(GetScopedPath(stagingRoot, releaseId));
        return Task.CompletedTask;
    }

    public Task<int> CleanStagingAsync(DateTimeOffset olderThan, CancellationToken cancellationToken)
    {
        var removed = 0;
        if (!Directory.Exists(stagingRoot))
        {
            return Task.FromResult(removed);
        }

        foreach (var directory in Directory.EnumerateDirectories(stagingRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var info = new DirectoryInfo(directory);
            if (info.LastWriteTimeUtc >= olderThan.UtcDateTime)
            {
                continue;
            }

            DeleteDirectoryIfPresent(directory);
            removed++;
        }

        return Task.FromResult(removed);
    }

    public async Task<bool> CheckReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(root);
            var probe = Path.Combine(root, $".ready-{Guid.NewGuid():N}");
            await File.WriteAllTextAsync(probe, timeProvider.GetUtcNow().ToString("O"), cancellationToken);
            File.Delete(probe);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private async Task<(string Hash, long Length)> WriteArchiveAsync(
        Stream source,
        string destination,
        string expectedHash,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[BufferSize];
        long total = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            if (total > options.MaxArchiveBytes)
            {
                throw Error(413, "CONTENT_ARCHIVE_TOO_LARGE", "内容归档文件超过上传大小限制");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        await output.FlushAsync(cancellationToken);
        var actualHash = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(actualHash),
                Convert.FromHexString(expectedHash)))
        {
            throw Error(422, "CONTENT_ARCHIVE_HASH_MISMATCH", "上传归档文件与 X-Artifact-Sha256 不匹配");
        }

        return (actualHash, total);
    }

    private async Task<ValidatedContentPackage> ValidateAndExtractAsync(
        ContentRelease release,
        string archivePath,
        string extractRoot,
        string archiveHash,
        long archiveSize,
        CancellationToken cancellationToken)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            var entries = new Dictionary<string, ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);
            long expandedSize = 0;
            foreach (var entry in archive.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (IsDirectory(entry))
                {
                    continue;
                }

                if (IsSymbolicLink(entry))
                {
                    throw InvalidPackage("内容归档文件中不允许包含符号链接");
                }

                var normalized = NormalizeRelativePath(entry.FullName);
                if (!entries.TryAdd(normalized, entry))
                {
                    throw InvalidPackage($"内容归档文件包含重复路径：{normalized}");
                }

                expandedSize = checked(expandedSize + entry.Length);
                if (expandedSize > options.MaxExpandedBytes)
                {
                    throw Error(413, "CONTENT_ARCHIVE_EXPANDED_TOO_LARGE", "内容归档解压后超过大小限制");
                }
            }

            if (entries.Count - (entries.ContainsKey("release-manifest.json") ? 1 : 0) > options.MaxFileCount)
            {
                throw Error(413, "CONTENT_ARCHIVE_FILE_LIMIT", "内容归档文件数量超过限制");
            }

            if (!entries.TryGetValue("release-manifest.json", out var manifestEntry))
            {
                throw InvalidPackage("归档根目录必须包含 release-manifest.json");
            }

            if (manifestEntry.Length is <= 0 or > MaxManifestBytes)
            {
                throw InvalidPackage("release-manifest.json 文件大小无效");
            }

            ReleasePackageManifest manifest;
            await using (var manifestStream = manifestEntry.Open())
            {
                manifest = await JsonSerializer.DeserializeAsync<ReleasePackageManifest>(
                    manifestStream,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
                    cancellationToken) ?? throw InvalidPackage("release-manifest.json 内容无效");
            }

            ValidateManifestIdentity(release, manifest);
            var declared = ValidateManifestFiles(manifest, entries);
            var validatedFiles = new List<ValidatedContentFile>(declared.Count);
            foreach (var pair in declared.OrderBy(value => value.Key, StringComparer.Ordinal))
            {
                var declaredFile = pair.Value;
                var entry = entries[pair.Key];
                if (entry.Length != declaredFile.Size)
                {
                    throw InvalidPackage($"文件大小与清单不一致：{pair.Key}");
                }

                var destination = ResolveWithin(extractRoot, pair.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                var actual = await ExtractAndHashAsync(entry, destination, cancellationToken);
                if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(actual),
                        Convert.FromHexString(declaredFile.Sha256)))
                {
                    throw InvalidPackage($"文件哈希与清单不一致：{pair.Key}");
                }

                validatedFiles.Add(new ValidatedContentFile(
                    pair.Key,
                    GetKind(pair.Key, manifest),
                    entry.Length,
                    actual));
            }

            var extractedManifest = ResolveWithin(extractRoot, "release-manifest.json");
            await ExtractEntryAsync(manifestEntry, extractedManifest, cancellationToken);
            return new ValidatedContentPackage(
                manifest,
                archiveHash,
                archiveSize,
                validatedFiles.Sum(value => value.Size),
                validatedFiles);
        }
        catch (InvalidDataException)
        {
            throw InvalidPackage("内容归档不是有效的 ZIP 文件");
        }
        catch (JsonException)
        {
            throw InvalidPackage("release-manifest.json 内容无效");
        }
        catch (OverflowException)
        {
            throw Error(413, "CONTENT_ARCHIVE_EXPANDED_TOO_LARGE", "内容归档解压后超过大小限制");
        }
    }

    private static Dictionary<string, ReleasePackageFile> ValidateManifestFiles(
        ReleasePackageManifest manifest,
        IReadOnlyDictionary<string, ZipArchiveEntry> entries)
    {
        if (manifest.Files is null || manifest.Files.Count == 0)
        {
            throw InvalidPackage("内容清单必须声明至少一个文件");
        }

        var declared = new Dictionary<string, ReleasePackageFile>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in manifest.Files)
        {
            var normalized = NormalizeRelativePath(file.Path);
            if (normalized.Equals("release-manifest.json", StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidPackage("release-manifest.json 不能声明自身");
            }

            if (file.Size < 0 || !ContentDeliveryValidation.IsSha256(file.Sha256))
            {
                throw InvalidPackage($"文件元数据无效：{normalized}");
            }

            var canonical = file with { Path = normalized, Sha256 = file.Sha256.ToLowerInvariant() };
            if (!declared.TryAdd(normalized, canonical))
            {
                throw InvalidPackage($"内容清单包含重复路径：{normalized}");
            }
        }

        var archivePaths = entries.Keys
            .Where(value => !value.Equals("release-manifest.json", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!archivePaths.SetEquals(declared.Keys))
        {
            throw InvalidPackage("归档文件与 release-manifest.json 声明不一致");
        }

        if (!manifest.HotUpdatePath.Equals("HybridCLR/HotUpdate.dll.bytes", StringComparison.Ordinal) ||
            !declared.ContainsKey(manifest.HotUpdatePath))
        {
            throw InvalidPackage("HotUpdatePath 必须指向 HybridCLR/HotUpdate.dll.bytes");
        }

        if (!IsAddressablesFile(manifest.CatalogPath, ".bin") || !declared.ContainsKey(manifest.CatalogPath))
        {
            throw InvalidPackage("CatalogPath 必须指向 Addressables .bin 文件");
        }

        if (!IsAddressablesFile(manifest.CatalogHashPath, ".hash") || !declared.ContainsKey(manifest.CatalogHashPath))
        {
            throw InvalidPackage("CatalogHashPath 必须指向 Addressables .hash 文件");
        }

        if (!declared.Keys.Any(value => IsAddressablesFile(value, ".bundle")))
        {
            throw InvalidPackage("至少需要一个 Addressables bundle 文件");
        }

        return declared;
    }

    private static void ValidateManifestIdentity(ContentRelease release, ReleasePackageManifest manifest)
    {
        if (manifest.SchemaVersion != 1)
        {
            throw InvalidPackage("目前仅支持 schemaVersion 1 的内容清单");
        }

        if (!string.Equals(release.Platform, manifest.Platform, StringComparison.Ordinal) ||
            !string.Equals(release.AppVersion, manifest.AppVersion, StringComparison.Ordinal) ||
            !string.Equals(release.ContentVersion, manifest.ContentVersion, StringComparison.Ordinal))
        {
            throw InvalidPackage("内容清单标识与已创建的内容版本不一致");
        }
    }

    private static async Task<string> ExtractAndHashAsync(
        ZipArchiveEntry entry,
        string destination,
        CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var input = entry.Open();
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        var buffer = new byte[BufferSize];
        long total = 0;
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0)
            {
                break;
            }

            total += read;
            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        if (total != entry.Length)
        {
            throw InvalidPackage($"解压后的文件长度不一致：{entry.FullName}");
        }

        await output.FlushAsync(cancellationToken);
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }

    private static async Task ExtractEntryAsync(
        ZipArchiveEntry entry,
        string destination,
        CancellationToken cancellationToken)
    {
        await using var input = entry.Open();
        await using var output = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, cancellationToken);
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > 512 || path.Contains('\\') || path.Contains('\0'))
        {
            throw InvalidPackage("内容路径不能为空，且必须是使用正斜杠的相对路径");
        }

        if (Path.IsPathRooted(path) || path.StartsWith('/') || path.Contains(':'))
        {
            throw InvalidPackage($"内容路径不能是绝对路径：{path}");
        }

        var segments = path.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".."))
        {
            throw InvalidPackage($"内容路径无效：{path}");
        }

        return string.Join('/', segments);
    }

    private static string ResolveWithin(string scopedRoot, string relativePath)
    {
        var fullRoot = Path.GetFullPath(scopedRoot);
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = fullRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw InvalidPackage($"内容路径超出存储根目录：{relativePath}");
        }

        return fullPath;
    }

    private static bool IsDirectory(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith('/') || string.IsNullOrEmpty(entry.Name);

    private static bool IsSymbolicLink(ZipArchiveEntry entry) =>
        ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000;

    private static bool IsAddressablesFile(string path, string extension) =>
        path.StartsWith("Addressables/", StringComparison.Ordinal) &&
        path.EndsWith(extension, StringComparison.OrdinalIgnoreCase) &&
        !path.Contains("//", StringComparison.Ordinal);

    private static string GetKind(string path, ReleasePackageManifest manifest)
    {
        if (path.Equals(manifest.HotUpdatePath, StringComparison.Ordinal))
        {
            return "hot-update";
        }

        if (path.Equals(manifest.CatalogPath, StringComparison.Ordinal))
        {
            return "catalog";
        }

        if (path.Equals(manifest.CatalogHashPath, StringComparison.Ordinal))
        {
            return "catalog-hash";
        }

        return path.EndsWith(".bundle", StringComparison.OrdinalIgnoreCase) ? "bundle" : "content";
    }

    private static ContentDeliveryException InvalidPackage(string message) =>
        Error(422, "INVALID_CONTENT_PACKAGE", message);

    private static ContentDeliveryException Error(int statusCode, string code, string message) =>
        new(statusCode, code, message);

    private static string GetScopedPath(string basePath, Guid releaseId) =>
        Path.Combine(basePath, releaseId.ToString("N"));

    private static void DeleteDirectoryIfPresent(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
