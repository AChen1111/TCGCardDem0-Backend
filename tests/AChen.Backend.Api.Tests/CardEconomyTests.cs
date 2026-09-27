using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.Players;
using AChen.Backend.Api.Features.Social;
using AChen.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AChen.Backend.Api.Tests;

public sealed class CardEconomyTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    const string Card = "26077389";
    const string Target = "01639384";
    readonly PublishedConfigFixture config = new(factory);

    [Theory]
    [InlineData(0, 2, 0, 2, 0)]
    [InlineData(2, 3, 0, 1, 20)]
    [InlineData(3, 2, 1, 0, 30)]
    [InlineData(5, 2, 0, 0, 20)]
    [InlineData(3, 1, 2, 0, 20)]
    [InlineData(3, 1, 3, 0, 25)]
    [InlineData(3, 1, 4, 0, 30)]
    public void Grant_only_converts_new_overflow(int owned, int granted, int rarity, int kept, long ur)
    {
        var rules = CardEconomyConfiguration.Load(PublishedConfigFixture.SourceFiles());
        var result = rules.Grant(owned, granted, rarity);
        Assert.Equal(kept, result.Kept);
        Assert.Equal(granted - kept, result.Overflow);
        Assert.Equal(ur, result.Ur);
    }

    [Fact]
    public void Sequential_grants_keep_independent_rarity_slots_and_report_each_draw()
    {
        var grants = new[] { new OwnedCard(Card, 0, 1), new OwnedCard(Card, 0, 1), new OwnedCard(Card, 1, 1) };
        var result = CardInventorySettlement.Grant([new OwnedCard(Card, 0, 2)], grants,
            CardEconomyConfiguration.Load(PublishedConfigFixture.SourceFiles()));
        Assert.Equal(new[] { 0, 1, 0 }, result.Results.Select(x => x.Overflow));
        Assert.Equal(10, result.UrGained);
        Assert.Contains(new OwnedCard(Card, 0, 3), result.Cards);
        Assert.Contains(new OwnedCard(Card, 1, 1), result.Cards);
    }

    [Fact]
    public async Task Gift_overflow_funds_crafting_and_last_copy_can_be_dismantled()
    {
        var (client, username) = await Login();
        using (client)
        {
            var gift = await Gift(client, username, [new(Card, 0, 6), new(Target, 4, 1)]);
            Assert.Equal(30, gift.UrGained);
            Assert.Equal(30, gift.Player.Ur);
            Assert.Contains(new OwnedCard(Card, 0, 3), gift.Player.OwnedCards);
            var crafted = await client.PostAsJsonAsync("/api/player/cards/craft", new CraftCardRequest(Target, gift.Player.Revision, 30));
            crafted.EnsureSuccessStatusCode();
            var result = (await crafted.Content.ReadFromJsonAsync<CardWorkshopResponse>())!;
            Assert.Equal(0, result.Player.Ur);
            Assert.Contains(new OwnedCard(Target, 0, 1), result.Player.OwnedCards);
            Assert.Contains(new OwnedCard(Target, 4, 1), result.Player.OwnedCards);
            var duplicate = await client.PostAsJsonAsync("/api/player/cards/craft", new CraftCardRequest(Target, result.Player.Revision, 30));
            await Error(duplicate, "NORMAL_CARD_ALREADY_OWNED");
            var decomposed = await client.PostAsJsonAsync("/api/player/cards/dismantle", new DismantleCardRequest(Target, 0, 1, result.Player.Revision, 10));
            decomposed.EnsureSuccessStatusCode();
            var after = (await decomposed.Content.ReadFromJsonAsync<CardWorkshopResponse>())!.Player;
            Assert.Equal(10, after.Ur);
            Assert.DoesNotContain(after.OwnedCards, x => x.CardId == Target && x.Rarity == 0);
            await Error(await client.PostAsJsonAsync("/api/player/cards/craft", new CraftCardRequest(Target, after.Revision, 30)), "INSUFFICIENT_UR");
            var loaded = (await client.GetFromJsonAsync<PlayerResponse>("/api/player/bootstrap"))!;
            Assert.Equal(after.Ur, loaded.Ur);
            Assert.Equal(after.Revision, loaded.Revision);
            var relogin = await client.PostAsJsonAsync("/api/auth/login", new { username, password = "correct-horse-42" });
            relogin.EnsureSuccessStatusCode();
            var login = await relogin.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal(after.Ur, login.GetProperty("player").GetProperty("ur").GetInt64());
            var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = login.GetProperty("refreshToken").GetString() });
            refresh.EnsureSuccessStatusCode();
            Assert.Equal(after.Ur, (await refresh.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("player").GetProperty("ur").GetInt64());
        }
    }

    [Fact]
    public async Task Draft_decks_protect_maximum_per_rarity_without_summing_different_decks()
    {
        var (client, username) = await Login();
        using (client)
        {
            var gift = await Gift(client, username, [new(Card, 0, 6)]);
            var deckA = await SaveDeck(client, "A", 2);
            var deckB = await SaveDeck(client, "B", 3);
            var before = await client.GetStringAsync("/api/player/bootstrap");
            var blocked = await client.PostAsJsonAsync("/api/player/cards/dismantle", new DismantleCardRequest(Card, 0, 1, gift.Player.Revision, 10));
            await Error(blocked, "CARD_IN_DECK");
            Assert.Contains("B", await blocked.Content.ReadAsStringAsync());
            Assert.Equal(before, await client.GetStringAsync("/api/player/bootstrap"));
            (await client.DeleteAsync($"/api/player/decks/{deckB}?expectedRevision=1")).EnsureSuccessStatusCode();
            var allowed = await client.PostAsJsonAsync("/api/player/cards/dismantle", new DismantleCardRequest(Card, 0, 1, gift.Player.Revision, 10));
            allowed.EnsureSuccessStatusCode();
            Assert.Equal(40, (await allowed.Content.ReadFromJsonAsync<CardWorkshopResponse>())!.Player.Ur);
            Assert.Equal(1, (await client.GetFromJsonAsync<JsonElement>($"/api/player/decks/{deckA}")).GetProperty("revision").GetInt64());
        }
    }

    [Fact]
    public async Task Legacy_excess_is_preserved_and_only_new_gifts_convert()
    {
        var (client, username) = await Login();
        using (client)
        {
            var player = (await client.GetFromJsonAsync<PlayerResponse>("/api/player/bootstrap"))!;
            using (var scope = factory.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var legacy = await db.PlayerProfiles.SingleAsync(x => x.UserId == player.Id);
                legacy.OwnedCards = [new(Card, 0, 5)];
                await db.SaveChangesAsync();
            }
            var gift = await Gift(client, username, [new(Card, 0, 2)]);
            Assert.Equal(20, gift.UrGained);
            Assert.Equal(5, Assert.Single(gift.Player.OwnedCards).Count);
        }
    }

    [Fact]
    public async Task Stale_prices_and_concurrent_crafts_never_double_spend()
    {
        var (client, username) = await Login();
        using (client)
        {
            var gift = await Gift(client, username, [new(Card, 0, 9)]);
            await Error(await client.PostAsJsonAsync("/api/player/cards/craft", new CraftCardRequest(Target, gift.Player.Revision, 29)), "CARD_PRICE_CHANGED");
            var request = new CraftCardRequest(Target, gift.Player.Revision, 30);
            var results = await Task.WhenAll(client.PostAsJsonAsync("/api/player/cards/craft", request), client.PostAsJsonAsync("/api/player/cards/craft", request));
            Assert.Single(results, x => x.IsSuccessStatusCode);
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
            var after = (await client.GetFromJsonAsync<PlayerResponse>("/api/player/bootstrap"))!;
            Assert.Equal(30, after.Ur);
            Assert.Equal(1, after.OwnedCards.Single(x => x.CardId == Target).Count);
            using var anonymous = factory.CreateClient();
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/player/cards/craft", request)).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/player/cards/dismantle", new {})).StatusCode);
        }
    }

    [Theory]
    [InlineData(0, 0, "INVALID_DISMANTLE_COUNT")]
    [InlineData(0, -1, "INVALID_DISMANTLE_COUNT")]
    [InlineData(5, 1, "INVALID_DISMANTLE_COUNT")]
    [InlineData(0, 4, "CARD_NOT_OWNED")]
    public async Task Invalid_dismantle_never_changes_assets(int rarity, int count, string code)
    {
        var (client, username) = await Login();
        using (client)
        {
            var gift = await Gift(client, username, [new(Card, 0, 3)]);
            var before = await client.GetStringAsync("/api/player/bootstrap");
            await Error(await client.PostAsJsonAsync("/api/player/cards/dismantle", new DismantleCardRequest(Card, rarity, count, gift.Player.Revision, count * 10)), code);
            Assert.Equal(before, await client.GetStringAsync("/api/player/bootstrap"));
        }
    }

    [Theory]
    [InlineData(0,10)][InlineData(1,15)][InlineData(2,20)][InlineData(3,25)][InlineData(4,30)]
    public async Task Every_version_can_dismantle_its_last_copies(int rarity,long reward)
    {
        var (client,name)=await Login();using(client)
        {
            var gift=await Gift(client,name,[new(Card,rarity,3)]);
            var response=await client.PostAsJsonAsync("/api/player/cards/dismantle",new DismantleCardRequest(Card,rarity,3,gift.Player.Revision,reward*3));
            response.EnsureSuccessStatusCode();var result=(await response.Content.ReadFromJsonAsync<CardWorkshopResponse>())!;
            Assert.Empty(result.Player.OwnedCards);Assert.Equal(reward*3,result.Player.Ur);Assert.Equal(reward*3,result.UrAmount);
            await Error(await client.PostAsJsonAsync("/api/player/cards/dismantle",new DismantleCardRequest(Card,rarity,3,gift.Player.Revision,reward*3)),"PLAYER_DATA_CHANGED");
        }
    }

    [Fact]
    public async Task Draw_endpoint_returns_overflow_in_order_and_updates_gold_ur_revision_atomically()
    {
        var pack=config.Data.Catalog.CardPacks.First(x=>x.PoolKey!="CardAll"&&x.IsEnabled);
        var selected=config.Data.PoolEntries.First(x=>x.PoolKey==pack.PoolKey);
        config.Data.PoolEntries=config.Data.PoolEntries.Where(x=>x.PoolKey!=pack.PoolKey||x.CardId==selected.CardId).ToArray();
        config.Data.RarityWeights=config.Data.RarityWeights.Where(x=>x.Rarity==0).ToArray();
        var (client,name)=await Login();using(client)
        {
            var gift=await Gift(client,name,[new(selected.CardId,0,2)]);
            using(var scope=factory.Services.CreateScope())
            {var db=scope.ServiceProvider.GetRequiredService<AppDbContext>();var p=await db.PlayerProfiles.SingleAsync(x=>x.UserId==gift.Player.Id);p.Gold=pack.PriceGold+123;await db.SaveChangesAsync();}
            var response=await client.PostAsJsonAsync("/api/player/card-draws",new DrawCardsRequest(pack.Id,pack.PoolKey,5,gift.Player.Revision));
            response.EnsureSuccessStatusCode();var draw=(await response.Content.ReadFromJsonAsync<DrawCardsResponse>())!;
            Assert.Equal(new[]{false,true,true,true,true},draw.Results.Select(x=>x.IsOverflow));
            Assert.Equal(new long[]{0,10,10,10,10},draw.Results.Select(x=>x.UrGained));
            Assert.Equal(40,draw.Player.Ur);Assert.Equal(123,draw.Player.Gold);Assert.Equal(gift.Player.Revision+1,draw.Player.Revision);
            Assert.Equal(3,Assert.Single(draw.Player.OwnedCards).Count);
        }
    }

    [Fact]
    public async Task Updated_quote_is_reported_and_other_accounts_decks_do_not_lock_inventory()
    {
        var (client,name)=await Login();using(client)
        {
            var gift=await Gift(client,name,[new(Card,0,6)]);
            var (other,otherName)=await Login();using(other) await SaveDeck(other,"另一个账号的卡组",3);
            var source=BinaryTable.Decode(config.EconomyTables["card-recycling"]);source.Rows[0][1]=12L;
            config.EconomyTables["card-recycling"]=source.Encode();await config.PublishAsync();
            var before=await client.GetStringAsync("/api/player/bootstrap");
            var stale=await client.PostAsJsonAsync("/api/player/cards/dismantle",new DismantleCardRequest(Card,0,3,gift.Player.Revision,30));
            var raw=await stale.Content.ReadAsStringAsync();await Error(stale,"CARD_PRICE_CHANGED");var body=JsonSerializer.Deserialize<JsonElement>(raw);
            Assert.Equal("36",body.GetProperty("errors").GetProperty("urAmount")[0].GetString());Assert.Equal(before,await client.GetStringAsync("/api/player/bootstrap"));
            var response=await client.PostAsJsonAsync("/api/player/cards/dismantle",new DismantleCardRequest(Card,0,3,gift.Player.Revision,36));
            response.EnsureSuccessStatusCode();Assert.Equal(66,(await response.Content.ReadFromJsonAsync<CardWorkshopResponse>())!.Player.Ur);
        }
    }

    async Task<(HttpClient, string)> Login()
    {
        if (config.ReleaseId is null) await config.PublishAsync();
        string name = "Ur" + Guid.NewGuid().ToString("N")[..14];
        var client = factory.CreateClient();
        config.Attach(client);
        var response = await client.PostAsJsonAsync("/api/auth/register", new { username = name, password = "correct-horse-42" });
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("player").GetProperty("ur").GetInt64());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.GetProperty("accessToken").GetString());
        return (client, name);
    }
    async Task<ClaimGiftResponse> Gift(HttpClient client, string name, OwnedCard[] cards)
    {
        var player = (await client.GetFromJsonAsync<PlayerResponse>("/api/player/bootstrap"))!;
        using var admin = factory.CreateClient();
        admin.DefaultRequestHeaders.Add("X-Content-Publish-Key", ApiFactory.PublishKey);
        var grant = await admin.PostAsJsonAsync("/api/accounts/admin/gifts", new { username = name, gold = 0, cards });
        grant.EnsureSuccessStatusCode();
        var id = (await grant.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("giftId").GetGuid();
        var claim = await client.PostAsJsonAsync($"/api/gifts/{id}/claim", new { expectedRevision = player.Revision });
        claim.EnsureSuccessStatusCode();
        return (await claim.Content.ReadFromJsonAsync<ClaimGiftResponse>())!;
    }
    static async Task<Guid> SaveDeck(HttpClient client, string name, int count)
    {
        var create = await client.PostAsJsonAsync("/api/player/decks", new { name });
        var id = (await create.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();
        var response = await client.PutAsJsonAsync($"/api/player/decks/{id}", new { name, expectedRevision = 0,
            mainDeck = new[] { new OwnedCard(Card, 0, 1) }, extraDeck = new[] { new OwnedCard(Card, 0, count - 1) } });
        response.EnsureSuccessStatusCode();
        return id;
    }
    static async Task Error(HttpResponseMessage response, string code)
    {
        Assert.False(response.IsSuccessStatusCode);
        Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    }
}
