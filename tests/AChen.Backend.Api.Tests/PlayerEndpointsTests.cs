using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AChen.Backend.Api.Features.GameConfig;
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
        using var client = await CreateAuthenticatedClientAsync("BasicPlayer", "basic@example.com");

        var response = await client.GetAsync("/api/player/bootstrap");
        response.EnsureSuccessStatusCode();
        var player = await response.Content.ReadFromJsonAsync<PlayerPayload>();

        Assert.NotNull(player);
        Assert.NotEqual(Guid.Empty, player.Id);
        Assert.Equal("BasicPlayer", player.Nickname);
        Assert.Null(player.AvatarId);
        Assert.Equal(0, player.Gold);
        Assert.Equal(0, player.Revision);
    }

    [Fact]
    public async Task Profile_update_changes_editable_fields_but_not_gold()
    {
        await EnsurePublishedAvatarAsync(2002);
        using var client = await CreateAuthenticatedClientAsync("EditPlayer", "edit@example.com");
        var initial = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.NotNull(initial);

        var response = await client.PatchAsJsonAsync("/api/player/profile", new
        {
            nickname = "新昵称",
            avatarId = 2002,
            expectedRevision = initial.Revision,
            gold = 999999
        });
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<PlayerPayload>();

        Assert.NotNull(updated);
        Assert.Equal("新昵称", updated.Nickname);
        Assert.Equal(2002, updated.AvatarId);
        Assert.Equal(0, updated.Gold);
        Assert.Equal(1, updated.Revision);
    }

    [Fact]
    public async Task Stale_profile_revision_is_rejected()
    {
        using var client = await CreateAuthenticatedClientAsync("RevisionPlayer", "revision@example.com");
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

    [Theory]
    [InlineData(" ", null)]
    [InlineData("Valid Name", -1)]
    public async Task Profile_update_validates_nickname_and_avatar(string nickname, int? avatarId)
    {
        using var client = await CreateAuthenticatedClientAsync(
            "Validation" + Guid.NewGuid().ToString("N")[..8],
            $"{Guid.NewGuid():N}@example.com");

        var response = await client.PatchAsJsonAsync("/api/player/profile", new
        {
            nickname,
            avatarId,
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
            "MissingAvatar",
            $"{Guid.NewGuid():N}@example.com");

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
        using var client = await CreateAuthenticatedClientAsync("DisabledAvatar", "disabled-avatar@example.com");
        var selected = await client.PatchAsJsonAsync("/api/player/profile", new
        {
            nickname = "Before Disable",
            avatarId,
            expectedRevision = 0
        });
        selected.EnsureSuccessStatusCode();

        await SetAvatarEnabledAndPublishAsync(avatarId, false);
        var nicknameOnly = await client.PatchAsJsonAsync("/api/player/profile", new
        {
            nickname = "After Disable",
            avatarId,
            expectedRevision = 1
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
        using var first = await CreateAuthenticatedClientAsync("FirstPlayer", "first-player@example.com");
        using var second = await CreateAuthenticatedClientAsync("SecondPlayer", "second-player@example.com");

        var firstPlayer = await first.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        var secondPlayer = await second.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");

        Assert.NotNull(firstPlayer);
        Assert.NotNull(secondPlayer);
        Assert.NotEqual(firstPlayer.Id, secondPlayer.Id);
        Assert.Equal("FirstPlayer", firstPlayer.Nickname);
        Assert.Equal("SecondPlayer", secondPlayer.Nickname);
    }

    private async Task<HttpClient> CreateAuthenticatedClientAsync(string username, string email)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            username,
            email,
            password = "correct-horse-42"
        });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthPayload>();
        Assert.NotNull(auth);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private async Task EnsurePublishedAvatarAsync(int id)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<GameConfigService>();
        var admin = await service.GetAdminAsync(CancellationToken.None);
        await service.UpsertAvatarAsync(
            new AvatarDefinitionInput(
                id,
                $"Avatar {id}",
                $"Avatar_{id}",
                id,
                true,
                admin.EditRevision),
            CancellationToken.None);
        admin = await service.GetAdminAsync(CancellationToken.None);
        await service.PublishAsync(admin.EditRevision, CancellationToken.None);
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
        long Gold,
        long Revision);
}
