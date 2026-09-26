using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AChen.Backend.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AChen.Backend.Api.Tests;

public sealed class PublishedConfigEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Draw_uses_published_pool_and_charges_published_price()
    {
        var config = new PublishedConfigFixture(factory);
        await config.PublishAsync();
        using var player = await PlayerAsync(config);
        var pack = config.Data.Catalog.CardPacks.First(x => x.IsEnabled);
        var response = await player.PostAsJsonAsync("/api/player/card-draws", new
        { packId = pack.Id, poolKey = pack.PoolKey, count = 2, expectedRevision = 0 });
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(2, result.RootElement.GetProperty("results").GetArrayLength());
        var allowedCards = pack.PoolKey == "CardAll"
            ? config.Data.AllCards.Select(x => x.CardId).ToHashSet()
            : config.Data.PoolEntries.Where(x => x.PoolKey == pack.PoolKey).Select(x => x.CardId).ToHashSet();
        foreach (var draw in result.RootElement.GetProperty("results").EnumerateArray())
        {
            Assert.Contains(draw.GetProperty("cardId").GetString(), allowedCards);
            Assert.Contains(draw.GetProperty("rarity").GetInt32(), config.Data.RarityWeights.Select(x => x.Rarity));
        }
        Assert.Equal(10000 - pack.PriceGold, result.RootElement.GetProperty("player").GetProperty("gold").GetInt64());
    }

    [Fact]
    public async Task Changed_config_requires_restart_before_draw()
    {
        var config = new PublishedConfigFixture(factory);
        await config.PublishAsync();
        using var player = await PlayerAsync(config);
        config.Data.Catalog.CardPacks.First(x => x.IsEnabled).PriceGold += 1;
        await config.PublishAsync(updateClients: false);
        player.DefaultRequestHeaders.Remove("X-Content-Release");
        var pack = config.Data.Catalog.CardPacks.First(x => x.IsEnabled);
        var response = await player.PostAsJsonAsync("/api/player/card-draws", new
        { packId = pack.Id, poolKey = pack.PoolKey, count = 1, expectedRevision = 0 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("CONTENT_CHANGED", await response.Content.ReadAsStringAsync());

    }

    [Fact]
    public async Task Missing_content_context_cannot_purchase()
    {
        var config = new PublishedConfigFixture(factory);
        await config.PublishAsync();
        using var player = await PlayerAsync(config);
        player.DefaultRequestHeaders.Remove("X-Config-Hash");
        var response = await player.PostAsJsonAsync("/api/player/purchase", new
        { catalogType = "avatar", itemId = 2, expectedRevision = 0 });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Legacy_configuration_routes_are_removed()
    {
        using var client = factory.CreateClient();
        foreach (var route in new[] { "/api/game-config/bootstrap", "/api/game-config/admin/draft", "/api/gacha/admin/config", "/api/gacha/admin/cards" })
            Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(route)).StatusCode);
    }

    private async Task<HttpClient> PlayerAsync(PublishedConfigFixture config)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        { username = "P" + Guid.NewGuid().ToString("N").Substring(0, 20), password = "correct-horse-42" });
        response.EnsureSuccessStatusCode();
        using var auth = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.RootElement.GetProperty("accessToken").GetString());
        config.Attach(client);
        var userId = auth.RootElement.GetProperty("user").GetProperty("id").GetGuid();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await db.PlayerProfiles.SingleAsync(x => x.UserId == userId);
        profile.Gold = 10000;
        await db.SaveChangesAsync();
        return client;
    }
}
