using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AChen.Backend.Api.Tests;

public sealed class AvatarFrameTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Frame_purchase_equip_and_profile_edit_preserve_inventory_and_gold()
    {
        var config = new PublishedConfigFixture(factory);
        await config.PublishAsync();
        using var client = factory.CreateClient();
        var registration = await client.PostAsJsonAsync("/api/auth/register", new { username = "FrameFlow", password = "correct-horse-42" });
        registration.EnsureSuccessStatusCode();
        var auth = (await registration.Content.ReadFromJsonAsync<Auth>())!;
        config.Attach(client);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var player = (await client.GetFromJsonAsync<PlayerResponse>("/api/player/bootstrap"))!;
        Assert.Equal(1010001, player.AvatarId);
        Assert.Equal(1030001, player.AvatarFrameId);
        Assert.Equal(new[] { 1030001 }, player.OwnedAvatarFrameIds);

        var unowned = await client.PatchAsJsonAsync("/api/player/profile", new UpdatePlayerProfileRequest(player.Nickname, player.AvatarId, player.BackgroundId, player.Revision, 1030002));
        Assert.True(unowned.StatusCode == HttpStatusCode.UnprocessableEntity, await unowned.Content.ReadAsStringAsync());
        var insufficient = await client.PostAsJsonAsync("/api/player/purchase", new PurchaseShopItemRequest("avatar-frame", 1030002, player.Revision));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, insufficient.StatusCode);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.PlayerProfiles.SingleAsync(x => x.UserId == player.Id);
            entity.Gold = 900; await db.SaveChangesAsync();
        }
        var purchase = await client.PostAsJsonAsync("/api/player/purchase", new PurchaseShopItemRequest("avatar-frame", 1030002, player.Revision));
        purchase.EnsureSuccessStatusCode(); player = (await purchase.Content.ReadFromJsonAsync<PlayerResponse>())!;
        Assert.Equal(400, player.Gold);
        Assert.Equal(new[] { 1030001, 1030002 }, player.OwnedAvatarFrameIds);
        var duplicate = await client.PostAsJsonAsync("/api/player/purchase", new PurchaseShopItemRequest("avatar-frame", 1030002, player.Revision));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, duplicate.StatusCode);
        var equip = await client.PatchAsJsonAsync("/api/player/profile", new UpdatePlayerProfileRequest(player.Nickname, player.AvatarId, player.BackgroundId, player.Revision, 1030002));
        equip.EnsureSuccessStatusCode(); player = (await equip.Content.ReadFromJsonAsync<PlayerResponse>())!;
        Assert.Equal(1030002, player.AvatarFrameId);
        var rename = await client.PatchAsJsonAsync("/api/player/profile", new UpdatePlayerProfileRequest("新名字", player.AvatarId, player.BackgroundId, player.Revision));
        rename.EnsureSuccessStatusCode();
        var persisted = (await client.GetFromJsonAsync<PlayerResponse>("/api/player/bootstrap"))!;
        Assert.Equal(1030002, persisted.AvatarFrameId);
        Assert.Equal(400, persisted.Gold);
        Assert.Equal(new[] { 1010001 }, persisted.OwnedAvatarIds);
        Assert.Equal(new[] { 1030001, 1030002 }, persisted.OwnedAvatarFrameIds);
    }
    sealed record Auth(string AccessToken);
}
