using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AChen.Backend.Api.Tests;

public sealed class ContentDeliveryEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient client = CreatePublisherClient(factory);

    [Fact]
    public async Task Management_endpoints_require_publish_key()
    {
        using var anonymous = factory.CreateClient();
        var response = await anonymous.PostAsJsonAsync("/api/content/releases", new
        {
            platform = "StandaloneWindows64",
            appVersion = "0.1.0",
            contentVersion = "1.0.0"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorCodeAsync(response, "INVALID_CONTENT_PUBLISH_KEY");
    }

    [Fact]
    public async Task Release_can_be_uploaded_published_and_downloaded_with_range_and_etag()
    {
        const string appVersion = "0.1.1";
        var release = await CreateReleaseAsync("1.1.0", appVersion);
        var package = BuildPackage("StandaloneWindows64", appVersion, "1.1.0");
        var upload = await UploadAsync(release.Id, package);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);

        var publish = await client.PutAsJsonAsync(
            $"/api/content/active-releases/development/StandaloneWindows64/{appVersion}",
            new { releaseId = release.Id, expectedCurrentReleaseId = (Guid?)null });
        Assert.Equal(HttpStatusCode.OK, publish.StatusCode);

        var manifest = await client.GetFromJsonAsync<LatestManifest>(
            $"/api/content/manifests/latest?channel=development&platform=StandaloneWindows64&appVersion={appVersion}");
        Assert.NotNull(manifest);
        Assert.Equal(release.Id, manifest.ReleaseId);
        Assert.Equal("1.1.0", manifest.ContentVersion);

        using var request = new HttpRequestMessage(HttpMethod.Get, manifest.HotUpdate.Path);
        request.Headers.Range = new RangeHeaderValue(0, 2);
        var download = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.PartialContent, download.StatusCode);
        Assert.Equal(3, (await download.Content.ReadAsByteArrayAsync()).Length);
        Assert.NotNull(download.Headers.ETag);
        Assert.Contains("immutable", download.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Duplicate_release_identity_is_rejected()
    {
        await CreateReleaseAsync("1.2.0");
        var duplicate = await client.PostAsJsonAsync("/api/content/releases", new
        {
            platform = "StandaloneWindows64",
            appVersion = "0.1.0",
            contentVersion = "1.2.0"
        });

        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        await AssertErrorCodeAsync(duplicate, "CONTENT_RELEASE_EXISTS");
    }

    [Fact]
    public async Task Path_traversal_archive_is_rejected()
    {
        var release = await CreateReleaseAsync("1.3.0");
        var package = BuildPackage(
            "StandaloneWindows64",
            "0.1.0",
            "1.3.0",
            archiveMutation: archive => archive.CreateEntry("../outside.bundle"));
        var response = await UploadAsync(release.Id, package);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorCodeAsync(response, "INVALID_CONTENT_PACKAGE");
    }

    [Fact]
    public async Task Upload_is_idempotent_for_same_archive_and_conflicts_for_different_archive()
    {
        var release = await CreateReleaseAsync("1.4.0");
        var package = BuildPackage("StandaloneWindows64", "0.1.0", "1.4.0");
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(release.Id, package)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await UploadAsync(release.Id, package)).StatusCode);

        var changed = BuildPackage(
            "StandaloneWindows64",
            "0.1.0",
            "1.4.0",
            bundleBytes: [9, 8, 7, 6]);
        var conflict = await UploadAsync(release.Id, changed);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        await AssertErrorCodeAsync(conflict, "CONTENT_ARTIFACT_CONFLICT");
    }

    [Fact]
    public async Task Active_release_uses_optimistic_concurrency_and_supports_rollback()
    {
        const string appVersion = "0.1.2";
        var first = await CreateReadyReleaseAsync("1.5.0", appVersion);
        var second = await CreateReadyReleaseAsync("1.6.0", appVersion);
        var initial = await SetActiveAsync(first.Id, null, appVersion);
        Assert.Equal(HttpStatusCode.OK, initial.StatusCode);

        var stale = await SetActiveAsync(second.Id, null, appVersion);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await AssertErrorCodeAsync(stale, "ACTIVE_RELEASE_CHANGED");

        Assert.Equal(HttpStatusCode.OK, (await SetActiveAsync(second.Id, first.Id, appVersion)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SetActiveAsync(first.Id, second.Id, appVersion)).StatusCode);
        var history = await client.GetFromJsonAsync<PublicationPage>(
            $"/api/content/publications?channel=development&platform=StandaloneWindows64&appVersion={appVersion}&pageSize=10");
        Assert.NotNull(history);
        Assert.Equal(3, history.Total);
    }

    [Fact]
    public async Task Admin_login_page_does_not_expose_publish_key()
    {
        using var anonymous = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await anonymous.GetAsync("/admin/content/login");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.DoesNotContain(ApiFactory.PublishKey, html, StringComparison.Ordinal);
        Assert.Contains("发布后台", html, StringComparison.Ordinal);
    }

    private async Task<ReleasePayload> CreateReadyReleaseAsync(string contentVersion, string appVersion = "0.1.0")
    {
        var release = await CreateReleaseAsync(contentVersion, appVersion);
        var package = BuildPackage("StandaloneWindows64", appVersion, contentVersion);
        var upload = await UploadAsync(release.Id, package);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        return release;
    }

    private async Task<ReleasePayload> CreateReleaseAsync(string contentVersion, string appVersion = "0.1.0")
    {
        var response = await client.PostAsJsonAsync("/api/content/releases", new
        {
            platform = "StandaloneWindows64",
            appVersion,
            contentVersion,
            notes = "integration test"
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ReleasePayload>())!;
    }

    private Task<HttpResponseMessage> SetActiveAsync(
        Guid releaseId,
        Guid? expected,
        string appVersion = "0.1.0") =>
        client.PutAsJsonAsync(
            $"/api/content/active-releases/development/StandaloneWindows64/{appVersion}",
            new { releaseId, expectedCurrentReleaseId = expected });

    private Task<HttpResponseMessage> UploadAsync(Guid releaseId, byte[] package)
    {
        using var hash = SHA256.Create();
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/content/releases/{releaseId:D}/artifact")
        {
            Content = new ByteArrayContent(package)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        request.Headers.Add("X-Artifact-Sha256", Convert.ToHexString(hash.ComputeHash(package)).ToLowerInvariant());
        return client.SendAsync(request);
    }

    private static byte[] BuildPackage(
        string platform,
        string appVersion,
        string contentVersion,
        byte[]? bundleBytes = null,
        Action<ZipArchive>? archiveMutation = null)
    {
        var files = new Dictionary<string, byte[]>
        {
            ["HybridCLR/HotUpdate.dll.bytes"] = [1, 2, 3, 4, 5],
            ["Addressables/catalog_0.1.0.bin"] = [6, 7, 8],
            ["Addressables/catalog_0.1.0.hash"] = Encoding.UTF8.GetBytes("catalog-hash"),
            ["Addressables/game.bundle"] = bundleBytes ?? [10, 11, 12]
        };
        var manifest = new
        {
            schemaVersion = 1,
            platform,
            appVersion,
            contentVersion,
            hotUpdatePath = "HybridCLR/HotUpdate.dll.bytes",
            catalogPath = "Addressables/catalog_0.1.0.bin",
            catalogHashPath = "Addressables/catalog_0.1.0.hash",
            files = files.Select(value => new
            {
                path = value.Key,
                size = value.Value.LongLength,
                sha256 = Convert.ToHexString(SHA256.HashData(value.Value)).ToLowerInvariant()
            }).ToArray()
        };

        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var file in files)
            {
                var entry = archive.CreateEntry(file.Key, CompressionLevel.NoCompression);
                using var output = entry.Open();
                output.Write(file.Value);
            }

            var manifestEntry = archive.CreateEntry("release-manifest.json", CompressionLevel.NoCompression);
            using (var output = manifestEntry.Open())
            {
                JsonSerializer.Serialize(output, manifest);
            }

            archiveMutation?.Invoke(archive);
        }

        return stream.ToArray();
    }

    private static HttpClient CreatePublisherClient(ApiFactory factory)
    {
        var result = factory.CreateClient();
        result.DefaultRequestHeaders.Add("X-Content-Publish-Key", ApiFactory.PublishKey);
        return result;
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
    }

    private sealed record ReleasePayload(Guid Id);
    private sealed record LatestManifest(Guid ReleaseId, string ContentVersion, HotUpdatePayload HotUpdate);
    private sealed record HotUpdatePayload(string Path, long Size, string Sha256);
    private sealed record PublicationPage(int Total);
}
