using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AChen.Configuration;
using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.ContentDelivery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
namespace AChen.Backend.Api.Tests;
public sealed class ContentDeliveryEndpointsTests
{
    [Fact]
    public async Task Status_requires_key_and_reports_shared_protocol()
    {
        using var factory=new ApiFactory(); using var client=factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await client.GetAsync("/api/dev/status")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Content-Publish-Key",ApiFactory.PublishKey);
        using var json=JsonDocument.Parse(await client.GetStringAsync("/api/dev/status"));
        Assert.Equal(DevelopmentProtocol.Version,json.RootElement.GetProperty("protocol").GetInt32());
    }
    [Fact]
    public async Task Replacing_latest_removes_old_files_and_keeps_one_row()
    {
        using var factory=new ApiFactory(); using var client=factory.CreateClient();
        var bytes=PublishedConfigFixture.Package("Android",PublishedConfigFixture.SourceFiles());
        var first=await Publish(client,"Android",bytes);
        var second=await Publish(client,"Android",bytes);
        Assert.NotEqual(first.contentId,second.contentId);
        Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync($"/content/current/Android/{first.contentId}/HybridCLR/HotUpdate.dll")).StatusCode);
        Assert.Equal(new byte[]{1,2},await client.GetByteArrayAsync($"/content/current/Android/{second.contentId}/HybridCLR/HotUpdate.dll"));
        Assert.Single(Directory.GetDirectories(Path.Combine(factory.ContentPath,"current","Android")));
        Assert.True(File.Exists(Path.Combine(factory.ContentPath,"current","Android","1.0.1_安卓","HybridCLR","HotUpdate.dll")));
        var stored=JsonSerializer.Deserialize<DevelopmentManifest>(File.ReadAllText(Path.Combine(factory.ContentPath,"current","Android","1.0.1_安卓","manifest.json")),LatestContentService.Json);
        Assert.Equal(second.contentId,stored!.contentId);
        using var scope=factory.Services.CreateScope();
        Assert.Equal(1,await scope.ServiceProvider.GetRequiredService<AppDbContext>().CurrentContents.CountAsync());
    }
    [Fact]
    public async Task Invalid_player_upload_clears_current_and_preserves_other_platform_and_editor()
    {
        using var factory=new ApiFactory(); using var client=factory.CreateClient();
        var configs=PublishedConfigFixture.SourceFiles();
        var android=await Publish(client,"Android",PublishedConfigFixture.Package("Android",configs));
        var editor=await Publish(client,"Editor",PublishedConfigFixture.Package("Editor",configs));
        var windows=await Publish(client,"StandaloneWindows64",PublishedConfigFixture.Package("StandaloneWindows64",configs));
        Assert.Equal(android.contentId,(await client.GetFromJsonAsync<DevelopmentManifest>("/api/content/latest/Android",LatestContentService.Json))!.contentId);
        using var bad=await PublishedConfigFixture.Upload(client,"Android",PublishedConfigFixture.Package("Android",configs,m=>m.schemaVersion=2));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,bad.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/content/latest/Android")).StatusCode);
        Assert.False(Directory.Exists(Path.Combine(factory.ContentPath,"current","Android")));
        Assert.Equal(editor.contentId,(await client.GetFromJsonAsync<DevelopmentManifest>("/api/content/latest/Editor",LatestContentService.Json))!.contentId);
        Assert.Equal(windows.contentId,(await client.GetFromJsonAsync<DevelopmentManifest>("/api/content/latest/StandaloneWindows64",LatestContentService.Json))!.contentId);
        using var badFile=await PublishedConfigFixture.Upload(client,"Android",PublishedConfigFixture.Package("Android",configs,m=>m.files[0].sha256=new string('0',64)));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,badFile.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync("/api/content/latest/iOS")).StatusCode);
    }
    [Fact]
    public async Task Config_hash_mismatch_is_rejected_and_path_traversal_is_rejected()
    {
        using var factory=new ApiFactory(); using var client=factory.CreateClient();
        var configs=PublishedConfigFixture.SourceFiles();
        await Publish(client,"Editor",PublishedConfigFixture.Package("Editor",configs));
        using var scope=factory.Services.CreateScope();
        var service=scope.ServiceProvider.GetRequiredService<LatestContentService>();
        var error=await Assert.ThrowsAsync<ContentDeliveryException>(()=>service.ConfigAsync("Editor","old",default));
        Assert.Equal("CONTENT_CHANGED",error.Code);
        using var response=await PublishedConfigFixture.Upload(client,"Android",PublishedConfigFixture.Package("Android",configs,m=>m.configs[0].path="../escape"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,response.StatusCode);
    }
    static async Task<DevelopmentManifest> Publish(HttpClient client,string target,byte[] bytes)
    {
        using var response=await PublishedConfigFixture.Upload(client,target,bytes);
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<DevelopmentManifest>(LatestContentService.Json))!;
    }

    [Fact]
    public async Task Interrupted_upload_clears_current_before_reading_body_and_releases_publish_lock()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        var bytes = PublishedConfigFixture.Package("Android", PublishedConfigFixture.SourceFiles());
        var first = await Publish(client, "Android", bytes);
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<LatestContentService>();
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await Assert.ThrowsAsync<IOException>(() => service.PublishAsync("Android", new InterruptedStream(() => {
                Assert.False(Directory.Exists(Path.Combine(factory.ContentPath,"current","Android")));
                Assert.False(db.CurrentContents.Any(x=>x.Target=="Android"));
            }), new string('a', 64), default));
            Assert.Equal("CONTENT_NOT_READY",(await Assert.ThrowsAsync<ContentDeliveryException>(()=>service.LatestAsync("Android",default))).Code);
            Assert.Empty(Directory.GetDirectories(Path.Combine(factory.ContentPath,"staging")));
        }
        await Publish(client, "Android", bytes);
    }
    sealed class InterruptedStream(Action beforeRead) : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            beforeRead();
            return ValueTask.FromException<int>(new IOException("模拟上传连接断开"));
        }
    }

    [Fact]
    public async Task Authentication_and_header_checks_leave_existing_content_untouched()
    {
        using var factory=new ApiFactory(); using var client=factory.CreateClient();
        var current=await Publish(client,"Android",PublishedConfigFixture.Package("Android",PublishedConfigFixture.SourceFiles()));
        using var unauthorized=await client.PutAsync("/api/dev/content/Android",new ByteArrayContent([]));
        Assert.Equal(HttpStatusCode.Unauthorized,unauthorized.StatusCode);
        using var request=new HttpRequestMessage(HttpMethod.Put,"/api/dev/content/Android") {Content=new ByteArrayContent([])};
        request.Headers.Add("X-Content-Publish-Key",ApiFactory.PublishKey);
        using var badHash=await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest,badHash.StatusCode);
        Assert.Equal(current.contentId,(await client.GetFromJsonAsync<DevelopmentManifest>("/api/content/latest/Android",LatestContentService.Json))!.contentId);
    }

    [Fact]
    public async Task Delete_failure_stops_before_body_read_and_preserves_current_record()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows 无 FileShare.Delete 的文件锁.
        using var factory=new ApiFactory(); using var client=factory.CreateClient();
        var bytes=PublishedConfigFixture.Package("Android",PublishedConfigFixture.SourceFiles());
        var first=await Publish(client,"Android",bytes);
        using var scope=factory.Services.CreateScope();
        var service=scope.ServiceProvider.GetRequiredService<LatestContentService>();
        using (var locked=File.Open(Path.Combine(factory.ContentPath,"current","Android","1.0.1_安卓","HybridCLR","HotUpdate.dll"),FileMode.Open,FileAccess.Read,FileShare.Read))
        {
            await Assert.ThrowsAsync<IOException>(()=>service.PublishAsync("Android",new InterruptedStream(()=>Assert.Fail("删除失败后不能接收上传")),new string('a',64),default));
            Assert.Equal(first.contentId,(await service.LatestAsync("Android",default)).contentId);
        }
        await Publish(client,"Android",bytes);
    }

    [Fact]
    public async Task Legacy_guid_directory_is_readable_and_replaced_without_deleting_other_targets()
    {
        using var factory=new ApiFactory(); using var client=factory.CreateClient();
        var bytes=PublishedConfigFixture.Package("Android",PublishedConfigFixture.SourceFiles());
        var first=await Publish(client,"Android",bytes);
        var editor=await Publish(client,"Editor",PublishedConfigFixture.Package("Editor",PublishedConfigFixture.SourceFiles()));
        string legacy=Path.Combine(factory.ContentPath,"current",first.contentId);
        Directory.Move(Path.Combine(factory.ContentPath,"current","Android","1.0.1_安卓"),legacy);
        using (var scope=factory.Services.CreateScope())
        {
            var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row=await db.CurrentContents.SingleAsync(x=>x.Target=="Android");
            first.schemaVersion=4; first.contentVersion=null!;
            row.ManifestJson=JsonSerializer.Serialize(first,LatestContentService.Json);
            await db.SaveChangesAsync();
        }
        Assert.Equal(new byte[]{1,2},await client.GetByteArrayAsync($"/content/current/Android/{first.contentId}/HybridCLR/HotUpdate.dll"));
        await Publish(client,"Android",PublishedConfigFixture.Package("Android",PublishedConfigFixture.SourceFiles(),m=>m.contentVersion="1.0.2"));
        Assert.False(Directory.Exists(legacy));
        Assert.True(Directory.Exists(Path.Combine(factory.ContentPath,"current","Android","1.0.2_安卓")));
        Assert.True(Directory.Exists(Path.Combine(factory.ContentPath,"current",editor.contentId)));
        string orphan=Path.Combine(factory.ContentPath,"current","Android","0.0.0_安卓");
        Directory.CreateDirectory(orphan);
        using (var scope=factory.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<LatestContentService>().CleanupAsync(default);
        Assert.False(Directory.Exists(orphan));
        Assert.True(Directory.Exists(Path.Combine(factory.ContentPath,"current","Android","1.0.2_安卓")));
        Assert.True(Directory.Exists(Path.Combine(factory.ContentPath,"current",editor.contentId)));
    }

    [Fact]
    public async Task Invalid_editor_upload_keeps_editor_and_player_content()
    {
        using var factory=new ApiFactory(); using var client=factory.CreateClient();
        var configs=PublishedConfigFixture.SourceFiles();
        var editor=await Publish(client,"Editor",PublishedConfigFixture.Package("Editor",configs));
        var android=await Publish(client,"Android",PublishedConfigFixture.Package("Android",configs));
        using var bad=await PublishedConfigFixture.Upload(client,"Editor",PublishedConfigFixture.Package("Editor",configs,m=>m.schemaVersion=2));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,bad.StatusCode);
        Assert.Equal(editor.contentId,(await client.GetFromJsonAsync<DevelopmentManifest>("/api/content/latest/Editor",LatestContentService.Json))!.contentId);
        Assert.Equal(android.contentId,(await client.GetFromJsonAsync<DevelopmentManifest>("/api/content/latest/Android",LatestContentService.Json))!.contentId);
    }
}
