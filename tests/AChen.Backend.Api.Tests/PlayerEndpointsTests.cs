using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.GameConfig;
using AChen.Backend.Api.Features.Players;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AChen.Backend.Api.Tests;

public sealed class PlayerEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Bootstrap_requires_access_token()
    {
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/api/player/bootstrap");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorCodeAsync(response, "INVALID_ACCESS_TOKEN");
    }

    [Fact]
    public async Task Registration_creates_default_player_data()
    {
        using var client = await CreateAuthenticatedClientAsync("BasicPlayer");

        var response = await client.GetAsync("/api/player/bootstrap");
        response.EnsureSuccessStatusCode();
        var player = await response.Content.ReadFromJsonAsync<PlayerPayload>();

        Assert.NotNull(player);
        Assert.NotEqual(Guid.Empty, player.Id);
        Assert.Equal("BasicPlayer", player.Nickname);
        Assert.Equal(0, player.AvatarId);
        Assert.Equal(new[] { 0 }, player.OwnedAvatarIds);
        Assert.Equal(1, player.BackgroundId);
        Assert.Equal(new[] { 1 }, player.OwnedBackgroundIds);
        Assert.Equal(0, player.Gold);
        Assert.Equal(0, player.Revision);
    }

    [Fact]
    public async Task Profile_update_changes_editable_fields_but_not_gold()
    {
        await EnsurePublishedAvatarAsync(2002);
        using var client = await CreateAuthenticatedClientAsync("EditPlayer");
        var initial = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(initial);
        await GrantAvatarAsync(initial.Id, 2002);
        initial = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(initial);

        var response = await client.PatchAsJsonAsync("/api/player/profile", new
        {
            nickname = "新昵称",
            avatarId = 2002,
            backgroundId = 1,
            expectedRevision = initial.Revision,
            gold = 999999
        });
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<PlayerPayload>();

        Assert.NotNull(updated);
        Assert.Equal("新昵称", updated.Nickname);
        Assert.Equal(2002, updated.AvatarId);
        Assert.Equal(new[] { 0, 2002 }, updated.OwnedAvatarIds);
        Assert.Equal(1, updated.BackgroundId);
        Assert.Equal(0, updated.Gold);
        Assert.Equal(initial.Revision + 1, updated.Revision);
    }

    [Fact]
    public async Task Profile_update_rejects_unowned_published_avatar()
    {
        await EnsurePublishedAvatarAsync(2002);
        using var client = await CreateAuthenticatedClientAsync("UnownedAvatar");

        var response = await client.PatchAsJsonAsync("/api/player/profile", new
        {
            nickname = "Valid Name",
            avatarId = 2002,
            expectedRevision = 0
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorCodeAsync(response, "AVATAR_NOT_OWNED");
    }

    [Fact]
    public async Task Profile_update_rejects_unowned_published_wallpaper()
    {
        await EnsurePublishedWallpaperAsync(3);
        using var client = await CreateAuthenticatedClientAsync("UnownedWallpaper");
        var response = await client.PatchAsJsonAsync("/api/player/profile", new
        {
            nickname = "Valid Name",
            avatarId = 0,
            backgroundId = 3,
            expectedRevision = 0
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorCodeAsync(response, "WALLPAPER_NOT_OWNED");
    }

    [Fact]
    public async Task Granting_avatar_adds_it_to_owned_list_without_equipping()
    {
        const int avatarId = 4004;
        await EnsurePublishedAvatarAsync(avatarId);
        using var client = await CreateAuthenticatedClientAsync("GrantAvatar");
        var initial = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(initial);

        var granted = await GrantAvatarAsync(initial.Id, avatarId);
        Assert.Equal(0, granted.AvatarId);
        Assert.Equal(new[] { 0, avatarId }, granted.OwnedAvatarIds);
        Assert.Equal(1, granted.Revision);

        var again = await GrantAvatarAsync(initial.Id, avatarId);
        Assert.Equal(new[] { 0, avatarId }, again.OwnedAvatarIds);
        Assert.Equal(1, again.Revision);
    }

    [Fact]
    public async Task Stale_profile_revision_is_rejected()
    {
        using var client = await CreateAuthenticatedClientAsync("RevisionPlayer");
        var initial = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(initial);

        var first = await client.PatchAsJsonAsync("/api/player/profile", new
        {
            nickname = "First Name",
            avatarId = (int?)null,
            expectedRevision = initial.Revision
        });
        first.EnsureSuccessStatusCode();

        var stale = await client.PatchAsJsonAsync("/api/player/profile", new
        {
            nickname = "Stale Name",
            avatarId = (int?)null,
            expectedRevision = initial.Revision
        });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await AssertErrorCodeAsync(stale, "PLAYER_DATA_CHANGED");
    }

    [Fact]
    public async Task Purchase_requires_access_token()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/player/purchase", new
        {
            catalogType = ShopCatalogTypes.Avatar,
            itemId = 1,
            expectedRevision = 0
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorCodeAsync(response, "INVALID_ACCESS_TOKEN");
    }

    [Fact]
    public async Task Purchase_avatar_deducts_gold_and_does_not_equip()
    {
        const int avatarId = 5101;
        await EnsurePublishedAvatarAsync(avatarId, 80);
        using var client = await CreateAuthenticatedClientAsync("BuyAvatar");
        var initial = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(initial);
        await SetGoldAsync(initial.Id, 200);

        var response = await client.PostAsJsonAsync("/api/player/purchase", new
        {
            catalogType = ShopCatalogTypes.Avatar,
            itemId = avatarId,
            expectedRevision = initial.Revision
        });
        response.EnsureSuccessStatusCode();
        var purchased = await response.Content.ReadFromJsonAsync<PlayerPayload>();

        Assert.NotNull(purchased);
        Assert.Equal(120, purchased.Gold);
        Assert.Equal(0, purchased.AvatarId);
        Assert.Equal(new[] { 0, avatarId }, purchased.OwnedAvatarIds);
        Assert.Equal(initial.Revision + 1, purchased.Revision);
    }

    [Fact]
    public async Task Purchase_wallpaper_deducts_gold_and_does_not_equip()
    {
        const int wallpaperId = 7;
        await EnsurePublishedWallpaperAsync(wallpaperId, 50);
        using var client = await CreateAuthenticatedClientAsync("BuyWallpaper");
        var initial = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(initial);
        await SetGoldAsync(initial.Id, 60);

        var response = await client.PostAsJsonAsync("/api/player/purchase", new
        {
            catalogType = ShopCatalogTypes.Wallpaper,
            itemId = wallpaperId,
            expectedRevision = initial.Revision
        });
        response.EnsureSuccessStatusCode();
        var purchased = await response.Content.ReadFromJsonAsync<PlayerPayload>();

        Assert.NotNull(purchased);
        Assert.Equal(10, purchased.Gold);
        Assert.Equal(1, purchased.BackgroundId);
        Assert.Equal(new[] { 1, wallpaperId }, purchased.OwnedBackgroundIds);
        Assert.Equal(initial.Revision + 1, purchased.Revision);
    }

    [Fact]
    public async Task Purchase_rejects_insufficient_gold()
    {
        const int avatarId = 5102;
        await EnsurePublishedAvatarAsync(avatarId, 100);
        using var client = await CreateAuthenticatedClientAsync("PoorBuyer");
        var initial = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(initial);

        var response = await client.PostAsJsonAsync("/api/player/purchase", new
        {
            catalogType = ShopCatalogTypes.Avatar,
            itemId = avatarId,
            expectedRevision = initial.Revision
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorCodeAsync(response, "INSUFFICIENT_GOLD");
        var current = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(current);
        Assert.Equal(0, current.Gold);
        Assert.Equal(new[] { 0 }, current.OwnedAvatarIds);
    }

    [Fact]
    public async Task Purchase_rejects_already_owned_item()
    {
        const int avatarId = 5103;
        await EnsurePublishedAvatarAsync(avatarId, 10);
        using var client = await CreateAuthenticatedClientAsync("OwnBuyer");
        var initial = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(initial);
        await GrantAvatarAsync(initial.Id, avatarId);
        await SetGoldAsync(initial.Id, 100);
        initial = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(initial);

        var response = await client.PostAsJsonAsync("/api/player/purchase", new
        {
            catalogType = ShopCatalogTypes.Avatar,
            itemId = avatarId,
            expectedRevision = initial.Revision
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorCodeAsync(response, "ITEM_ALREADY_OWNED");
        var current = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(current);
        Assert.Equal(100, current.Gold);
    }

    [Fact]
    public async Task Purchase_rejects_unavailable_item()
    {
        const int avatarId = 5104;
        await EnsurePublishedAvatarAsync(avatarId, 10);
        await SetAvatarEnabledAndPublishAsync(avatarId, false);
        using var client = await CreateAuthenticatedClientAsync("ClosedShop");
        var initial = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(initial);
        await SetGoldAsync(initial.Id, 100);

        var response = await client.PostAsJsonAsync("/api/player/purchase", new
        {
            catalogType = ShopCatalogTypes.Avatar,
            itemId = avatarId,
            expectedRevision = initial.Revision
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorCodeAsync(response, "AVATAR_NOT_AVAILABLE");
    }

    [Fact]
    public async Task Purchase_rejects_stale_revision()
    {
        const int avatarId = 5105;
        await EnsurePublishedAvatarAsync(avatarId, 10);
        using var client = await CreateAuthenticatedClientAsync("StaleBuyer");
        var initial = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(initial);
        await SetGoldAsync(initial.Id, 100);

        var first = await client.PostAsJsonAsync("/api/player/purchase", new
        {
            catalogType = ShopCatalogTypes.Avatar,
            itemId = avatarId,
            expectedRevision = initial.Revision
        });
        first.EnsureSuccessStatusCode();

        var stale = await client.PostAsJsonAsync("/api/player/purchase", new
        {
            catalogType = ShopCatalogTypes.Avatar,
            itemId = avatarId,
            expectedRevision = initial.Revision
        });

        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        await AssertErrorCodeAsync(stale, "PLAYER_DATA_CHANGED");
    }

    [Fact]
    public async Task Purchase_rejects_unknown_catalog_type()
    {
        using var client = await CreateAuthenticatedClientAsync("BadCatalog");
        var response = await client.PostAsJsonAsync("/api/player/purchase", new
        {
            catalogType = "cardPack",
            itemId = 1,
            expectedRevision = 0
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorCodeAsync(response, "VALIDATION_ERROR");
    }

    [Theory]
    [InlineData(" ", null, null)]
    [InlineData("Valid Name", -1, null)]
    [InlineData("Valid Name", null, -1)]
    public async Task Profile_update_validates_nickname_avatar_and_background(
        string nickname,
        int? avatarId,
        int? backgroundId)
    {
        using var client = await CreateAuthenticatedClientAsync(
            "Validation" + Guid.NewGuid().ToString("N")[..8]);

        var response = await client.PatchAsJsonAsync("/api/player/profile", new
        {
            nickname,
            avatarId,
            backgroundId,
            expectedRevision = 0
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorCodeAsync(response, "VALIDATION_ERROR");
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(problem.RootElement.TryGetProperty("errors", out _));
    }

    [Fact]
    public async Task Profile_update_rejects_avatar_outside_published_config()
    {
        using var client = await CreateAuthenticatedClientAsync(
            "MissingAvatar");

        var response = await client.PatchAsJsonAsync("/api/player/profile", new
        {
            nickname = "Valid Name",
            avatarId = 987654,
            expectedRevision = 0
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorCodeAsync(response, "AVATAR_NOT_AVAILABLE");
    }

    [Fact]
    public async Task Existing_disabled_avatar_can_remain_while_nickname_changes()
    {
        const int avatarId = 3003;
        await EnsurePublishedAvatarAsync(avatarId);
        using var client = await CreateAuthenticatedClientAsync("DisabledAvatar");
        var initial = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(initial);
        await GrantAvatarAsync(initial.Id, avatarId);
        var selected = await client.PatchAsJsonAsync("/api/player/profile", new
        {
            nickname = "Before Disable",
            avatarId,
            expectedRevision = 1
        });
        selected.EnsureSuccessStatusCode();

        await SetAvatarEnabledAndPublishAsync(avatarId, false);
        var nicknameOnly = await client.PatchAsJsonAsync("/api/player/profile", new
        {
            nickname = "After Disable",
            avatarId,
            expectedRevision = 2
        });
        nicknameOnly.EnsureSuccessStatusCode();
        var player = await nicknameOnly.Content.ReadFromJsonAsync<PlayerPayload>();
        Assert.NotNull(player);
        Assert.Equal(avatarId, player.AvatarId);
        Assert.Equal("After Disable", player.Nickname);
    }

    [Fact]
    public async Task Player_data_is_isolated_by_authenticated_user()
    {
        using var first = await CreateAuthenticatedClientAsync("FirstPlayer");
        using var second = await CreateAuthenticatedClientAsync("SecondPlayer");

        var firstPlayer = await first.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        var secondPlayer = await second.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");

        Assert.NotNull(firstPlayer);
        Assert.NotNull(secondPlayer);
        Assert.NotEqual(firstPlayer.Id, secondPlayer.Id);
        Assert.Equal("FirstPlayer", firstPlayer.Nickname);
        Assert.Equal("SecondPlayer", secondPlayer.Nickname);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            username,
            password = "correct-horse-42"
        });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthPayload>();
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private async Task<PlayerPayload> GrantAvatarAsync(Guid userId, int avatarId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<PlayerService>();
        var player = await service.GrantAvatarAsync(userId, avatarId, CancellationToken.None);
        return new PlayerPayload(
            player.Id,
            player.Nickname,
            player.AvatarId,
            player.OwnedAvatarIds,
            player.BackgroundId,
            player.OwnedBackgroundIds,
            player.Gold,
            player.Revision);
    }

    private async Task EnsurePublishedAvatarAsync(int id, long priceGold = 0)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<GameConfigService>();
        var admin = await service.GetAdminAsync(CancellationToken.None);
        await service.UpsertAvatarAsync(
            new AvatarDefinitionInput(
                id,
                $"Avatar {id}",
                $"Avatar_{id}",
                priceGold,
                id,
                true,
                admin.EditRevision),
            CancellationToken.None);
        admin = await service.GetAdminAsync(CancellationToken.None);
        await service.PublishAsync(admin.EditRevision, CancellationToken.None);
    }

    private async Task EnsurePublishedWallpaperAsync(int id, long priceGold = 0)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<GameConfigService>();
        var admin = await service.GetAdminAsync(CancellationToken.None);
        await service.UpsertWallpaperAsync(
            new WallpaperDefinitionInput(id, $"Wallpaper {id}", $"Wallpaper_{id}", priceGold, id, true, admin.EditRevision),
            CancellationToken.None);
        admin = await service.GetAdminAsync(CancellationToken.None);
        await service.PublishAsync(admin.EditRevision, CancellationToken.None);
    }

    private async Task SetGoldAsync(Guid userId, long gold)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var profile = await db.PlayerProfiles.SingleAsync(value => value.UserId == userId);
        profile.Gold = gold;
        await db.SaveChangesAsync();
    }

    private async Task SetAvatarEnabledAndPublishAsync(int id, bool isEnabled)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<GameConfigService>();
        var admin = await service.GetAdminAsync(CancellationToken.None);
        var avatar = admin.Avatars.Single(value => value.Id == id);
        await service.UpsertAvatarAsync(
            new AvatarDefinitionInput(
                avatar.Id,
                avatar.Name,
                avatar.ResourceKey,
                avatar.PriceGold,
                avatar.SortOrder,
                isEnabled,
                admin.EditRevision),
            CancellationToken.None);
        admin = await service.GetAdminAsync(CancellationToken.None);
        await service.PublishAsync(admin.EditRevision, CancellationToken.None);
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
        Assert.True(response.Headers.Contains("X-Request-Id"));
    }

    private sealed record AuthPayload(string AccessToken);

    private sealed record PlayerPayload(
        Guid Id,
        string Nickname,
        int? AvatarId,
        IReadOnlyList<int> OwnedAvatarIds,
        int? BackgroundId,
        IReadOnlyList<int> OwnedBackgroundIds,
        long Gold,
        long Revision);
}
