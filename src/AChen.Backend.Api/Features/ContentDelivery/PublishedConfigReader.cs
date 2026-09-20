using System.Security.Cryptography;
using System.Text.Json;
using AChen.Configuration;
using AChen.Backend.Api.Infrastructure;

namespace AChen.Backend.Api.Features.ContentDelivery;

public sealed class PublishedConfigReader(
    IContentReleaseRepository repository,
    IContentStorage storage,
    IHttpContextAccessor accessor,
    ILogger<PublishedConfigReader> logger)
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { IncludeFields = true };
    private PublishedGameConfig? snapshot;
    public Guid ReleaseId { get; private set; }

    public async Task<PublishedGameConfig> GetAsync(CancellationToken token)
    {
        if (snapshot is not null) return snapshot;
        var headers = accessor.HttpContext?.Request.Headers;
        var channel = headers?["X-Content-Channel"].ToString() ?? "";
        var platform = headers?["X-Content-Platform"].ToString() ?? "";
        var appVersion = headers?["X-Content-App-Version"].ToString() ?? "";
        ActiveContentRelease? active = null;
        if (!string.IsNullOrWhiteSpace(channel)
            && !string.IsNullOrWhiteSpace(platform)
            && !string.IsNullOrWhiteSpace(appVersion))
        {
            active = await repository.GetActiveAsync(channel, platform, appVersion, true, token);
        }

        if (active is null || active.Release.State != ContentReleaseState.Ready)
        {
            active = await repository.FindReadyActiveAsync(true, token);
        }

        if (active is null || active.Release.State != ContentReleaseState.Ready)
            throw new ApiException(503, "CONTENT_NOT_READY", "尚未发布可用游戏配置");
        snapshot = await ReadReleaseAsync(active.Release, token);
        ReleaseId = active.ReleaseId;
        return snapshot;
    }

    public async Task<PublishedGameConfig> ReadReleaseAsync(ContentRelease release, CancellationToken token)
    {
        try
        {
            var file = await repository.GetFileAsync(release.Id, PublishedGameConfig.PackagePath, token);
            if (file is null) throw new FormatException("发布版本缺少统一配置");
            var stored = await storage.OpenReadAsync(release.Id, PublishedGameConfig.PackagePath, token)
                ?? throw new FormatException("发布配置文件不存在");
            await using var stream = stored.Stream;
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, token);
            var bytes = buffer.ToArray();
            if (bytes.LongLength != file.Size || !Convert.ToHexString(SHA256.HashData(bytes)).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new FormatException("发布配置哈希不匹配");
            return Parse(bytes);
        }
        catch (Exception exception) when (exception is IOException or FormatException or JsonException or ArgumentException)
        {
            logger.LogError(exception, "Published configuration unavailable for release {ReleaseId}.", release.Id);
            throw new ApiException(503, "CONTENT_NOT_READY", "发布配置不可用，请重新发布");
        }
    }

    public static PublishedGameConfig Parse(byte[] bytes)
    {
        var value = JsonSerializer.Deserialize<PublishedGameConfig>(bytes, JsonOptions)
            ?? throw new FormatException("发布配置为空");
        value.Validate();
        return value;
    }
}
