using System.Text.Json;
using AChen.Configuration;
namespace AChen.Backend.Api.Features.ContentDelivery;
public sealed class PublishedConfigReader(LatestContentService content, IHttpContextAccessor accessor)
{
    public static readonly JsonSerializerOptions JsonOptions = LatestContentService.Json;
    PublishedGameConfig? snapshot;
    public Guid ReleaseId { get; private set; }
    public async Task<PublishedGameConfig> GetAsync(CancellationToken ct)
    {
        if (snapshot != null) return snapshot;
        var headers = accessor.HttpContext!.Request.Headers;
        var target = headers["X-Content-Target"].ToString();
        var hash = headers["X-Config-Hash"].ToString();
        var result = await content.ReadConfigAsync(target, hash, ct);
        snapshot = result.Data;
        ReleaseId = result.ContentId;
        return snapshot;
    }
}
