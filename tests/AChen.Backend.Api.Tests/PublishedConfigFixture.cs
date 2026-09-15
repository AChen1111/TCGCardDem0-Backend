using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AChen.Configuration;
using AChen.Backend.Api.Features.ContentDelivery;

namespace AChen.Backend.Api.Tests;

internal sealed class PublishedConfigFixture(ApiFactory factory)
{
    public PublishedGameConfig Data { get; } = PublishedConfigReader.Parse(SourceBytes());
    public string AppVersion { get; } = "test-" + Guid.NewGuid().ToString("N");
    public Guid? ReleaseId { get; private set; }
    private int version;
    private readonly List<HttpClient> clients = [];

    public static byte[] SourceBytes()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, "Assets/GameConfiguration/config.json");
            if (File.Exists(path)) return File.ReadAllBytes(path);
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Repository config fixture not found.");
    }

    public void Attach(HttpClient client)
    {
        clients.Add(client);
        SetHeaders(client);
    }

    public void SetHeaders(HttpClient client)
    {
        foreach (var header in new[] { "X-Content-Release", "X-Content-Channel", "X-Content-Platform", "X-Content-App-Version" })
            client.DefaultRequestHeaders.Remove(header);
        client.DefaultRequestHeaders.Add("X-Content-Release", ReleaseId.ToString());
        client.DefaultRequestHeaders.Add("X-Content-Channel", "development");
        client.DefaultRequestHeaders.Add("X-Content-Platform", "StandaloneWindows64");
        client.DefaultRequestHeaders.Add("X-Content-App-Version", AppVersion);
    }

    public async Task PublishAsync(bool updateClients = true)
    {
        using var publisher = factory.CreateClient();
        publisher.DefaultRequestHeaders.Add("X-Content-Publish-Key", ApiFactory.PublishKey);
        var contentVersion = "1.0." + ++version;
        var created = await publisher.PostAsJsonAsync("/api/content/releases", new
        { platform = "StandaloneWindows64", appVersion = AppVersion, contentVersion });
        created.EnsureSuccessStatusCode();
        var release = (await created.Content.ReadFromJsonAsync<Release>())!;
        var files = new Dictionary<string, byte[]>
        {
            ["HybridCLR/HotUpdate.dll.bytes"] = [1, 2],
            ["Addressables/catalog.bin"] = [3, 4],
            ["Addressables/catalog.hash"] = [5, 6],
            ["Addressables/config.bundle"] = [7, 8],
            [PublishedGameConfig.PackagePath] = JsonSerializer.SerializeToUtf8Bytes(Data, PublishedConfigReader.JsonOptions)
        };
        var manifest = new
        {
            schemaVersion = 2, platform = "StandaloneWindows64", appVersion = AppVersion, contentVersion,
            hotUpdatePath = "HybridCLR/HotUpdate.dll.bytes", catalogPath = "Addressables/catalog.bin",
            catalogHashPath = "Addressables/catalog.hash", configPath = PublishedGameConfig.PackagePath,
            files = files.Select(x => new { path = x.Key, size = x.Value.Length, sha256 = Convert.ToHexString(SHA256.HashData(x.Value)) }).ToArray()
        };
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, true))
        {
            foreach (var file in files)
            {
                using var output = zip.CreateEntry(file.Key).Open();
                output.Write(file.Value);
            }
            using var stream = zip.CreateEntry("release-manifest.json").Open();
            JsonSerializer.Serialize(stream, manifest);
        }
        var bytes = buffer.ToArray();
        using var upload = new HttpRequestMessage(HttpMethod.Put, $"/api/content/releases/{release.Id}/artifact") { Content = new ByteArrayContent(bytes) };
        upload.Content.Headers.ContentType = new("application/zip");
        upload.Headers.Add("X-Artifact-Sha256", Convert.ToHexString(SHA256.HashData(bytes)));
        (await publisher.SendAsync(upload)).EnsureSuccessStatusCode();
        (await publisher.PutAsJsonAsync($"/api/content/active-releases/development/StandaloneWindows64/{AppVersion}",
            new { releaseId = release.Id, expectedCurrentReleaseId = ReleaseId })).EnsureSuccessStatusCode();
        ReleaseId = release.Id;
        if (updateClients) foreach (var client in clients) SetHeaders(client);
    }

    private sealed record Release(Guid Id);
}
