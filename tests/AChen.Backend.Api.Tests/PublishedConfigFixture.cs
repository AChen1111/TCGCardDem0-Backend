using System.IO.Compression;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AChen.Configuration;
using AChen.Backend.Api.Features.ContentDelivery;
namespace AChen.Backend.Api.Tests;

internal sealed class PublishedConfigFixture(ApiFactory factory)
{
    public PublishedGameConfig Data { get; } = GameConfigTables.Assemble(SourceFiles());
    public string AppVersion { get; } = "0.1.0";
    public Guid? ReleaseId { get; private set; }
    public string ConfigHash { get; private set; } = "";
    readonly List<HttpClient> clients = [];
    public static Dictionary<string, byte[]> SourceFiles()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var root = Path.Combine(directory.FullName, "Assets/GameConfiguration");
            if (Directory.Exists(root)) return Directory.GetFiles(root, "*.bytes").ToDictionary(x => Path.GetFileNameWithoutExtension(x)!, File.ReadAllBytes);
            directory = directory.Parent;
        }
        throw new FileNotFoundException("Repository binary config fixture not found.");
    }
    public void Attach(HttpClient client) { clients.Add(client); SetHeaders(client); }
    public void SetHeaders(HttpClient client)
    {
        client.DefaultRequestHeaders.Remove("X-Content-Target"); client.DefaultRequestHeaders.Remove("X-Config-Hash");
        client.DefaultRequestHeaders.Add("X-Content-Target", "Editor"); client.DefaultRequestHeaders.Add("X-Config-Hash", ConfigHash);
    }
    public Dictionary<string, byte[]> Files() => new()
    {
        ["avatar-frames"] = GameConfigTables.FromRows(Data.Catalog.AvatarFrames).Encode(),
        ["avatars"] = GameConfigTables.FromRows(Data.Catalog.Avatars).Encode(),
        ["wallpapers"] = GameConfigTables.FromRows(Data.Catalog.Wallpapers).Encode(),
        ["card-packs"] = GameConfigTables.FromRows(Data.Catalog.CardPacks).Encode(),
        ["pool-entries"] = GameConfigTables.FromRows(Data.PoolEntries).Encode(),
        ["rarity-weights"] = GameConfigTables.FromRows(Data.RarityWeights).Encode(),
        ["all-cards"] = GameConfigTables.FromRows(Data.AllCards).Encode(),
        ["wallpaper-offsets"] = GameConfigTables.FromRows(Data.WallpaperOffsets).Encode(),
        ["Cards"] = Data.CardTable, ["Translations"] = Data.TranslationTable
    };
    public static byte[] Package(string target, Dictionary<string, byte[]> configs, Action<DevelopmentManifest>? mutate = null)
    {
        var files = configs.ToDictionary(x => GameConfigTables.PackagePath(x.Key), x => x.Value);
        if (target != "Editor")
        {
            files[DevelopmentProtocol.HotUpdatePath] = [1,2];
            files[DevelopmentProtocol.HotUpdatePath + ".sha256"] = System.Text.Encoding.UTF8.GetBytes(Convert.ToHexString(SHA256.HashData(files[DevelopmentProtocol.HotUpdatePath])));
            files["Addressables/catalog.bin"] = [3,4];
            files["Addressables/catalog.hash"] = [5,6];
            files["Addressables/config.bundle"] = [7,8];
        }
        var artifacts = configs.Select(x => new ConfigArtifact { category=x.Key, path=GameConfigTables.PackagePath(x.Key),
            format=GameConfigTables.Format(x.Key), address=GameConfigTables.Address(x.Key), size=x.Value.Length,
            sha256=Convert.ToHexString(SHA256.HashData(x.Value)).ToLowerInvariant() }).ToArray();
        var manifest = new DevelopmentManifest { platform=target, configs=artifacts, configHash=DevelopmentProtocol.ConfigHash(artifacts),
            contentVersion="1.0.1", hotUpdatePath=DevelopmentProtocol.HotUpdatePath,
            catalogPath="Addressables/catalog.bin", catalogHashPath="Addressables/catalog.hash",
            files=files.Select(x => new DevelopmentFile { path=x.Key,size=x.Value.Length,sha256=Convert.ToHexString(SHA256.HashData(x.Value)) }).ToArray() };
        mutate?.Invoke(manifest);
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create,true))
        {
            foreach (var file in files) { using var output=zip.CreateEntry(file.Key).Open(); output.Write(file.Value); }
            using var outputManifest=zip.CreateEntry("manifest.json").Open();
            JsonSerializer.Serialize(outputManifest,manifest,LatestContentService.Json);
        }
        return buffer.ToArray();
    }
    public static async Task<HttpResponseMessage> Upload(HttpClient client,string target,byte[] bytes)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put,target=="Editor"?"/api/dev/editor-config":"/api/dev/content/"+target)
            { Content=new ByteArrayContent(bytes) };
        request.Headers.Add("X-Artifact-Sha256",Convert.ToHexString(SHA256.HashData(bytes)));
        request.Headers.Add("X-Content-Publish-Key",ApiFactory.PublishKey);
        return await client.SendAsync(request);
    }
    public async Task PublishAsync(bool updateClients=true)
    {
        using var publisher=factory.CreateClient();
        using var response=await Upload(publisher,"Editor",Package("Editor",Files()));
        response.EnsureSuccessStatusCode();
        var manifest=(await response.Content.ReadFromJsonAsync<DevelopmentManifest>(LatestContentService.Json))!;
        ReleaseId=Guid.Parse(manifest.contentId); ConfigHash=manifest.configHash;
        if(updateClients) foreach(var client in clients) SetHeaders(client);
    }
}
