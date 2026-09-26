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
        Assert.Equal(HttpStatusCode.Conflict,(await client.GetAsync($"/content/current/Android/{first.contentId}/HybridCLR/HotUpdate.dll.bytes")).StatusCode);
        Assert.Equal(new byte[]{1,2},await client.GetByteArrayAsync($"/content/current/Android/{second.contentId}/HybridCLR/HotUpdate.dll.bytes"));
        using var scope=factory.Services.CreateScope();
        Assert.Equal(1,await scope.ServiceProvider.GetRequiredService<AppDbContext>().CurrentContents.CountAsync());
    }
    [Fact]
    public async Task Invalid_upload_keeps_current_and_editor_does_not_change_android()
    {
        using var factory=new ApiFactory(); using var client=factory.CreateClient();
        var configs=PublishedConfigFixture.SourceFiles();
        var android=await Publish(client,"Android",PublishedConfigFixture.Package("Android",configs));
        await Publish(client,"Editor",PublishedConfigFixture.Package("Editor",configs));
        using var bad=await PublishedConfigFixture.Upload(client,"Android",PublishedConfigFixture.Package("Android",configs,m=>m.schemaVersion=2));
        Assert.Equal(HttpStatusCode.UnprocessableEntity,bad.StatusCode);
        var current=await client.GetFromJsonAsync<DevelopmentManifest>("/api/content/latest/Android",LatestContentService.Json);
        Assert.Equal(android.contentId,current!.contentId);
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
    public async Task Interrupted_upload_preserves_current_and_does_not_hold_publish_lock()
    {
        using var factory = new ApiFactory(); using var client = factory.CreateClient();
        var bytes = PublishedConfigFixture.Package("Android", PublishedConfigFixture.SourceFiles());
        var first = await Publish(client, "Android", bytes);
        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<LatestContentService>();
            await Assert.ThrowsAsync<IOException>(() => service.PublishAsync("Android", new InterruptedStream(), new string('a', 64), default));
            Assert.Equal(first.contentId, (await service.LatestAsync("Android", default)).contentId);
        }
        await Publish(client, "Android", bytes);
    }
    sealed class InterruptedStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) =>
            ValueTask.FromException<int>(new IOException("模拟上传连接断开"));
    }
}
