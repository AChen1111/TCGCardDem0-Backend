using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using AChen.Backend.Api.Features.GameConfig;
using AChen.Backend.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace AChen.Backend.Api.Tests;

public sealed class GameConfigEndpointsTests
{
    [Fact]
    public async Task Bootstrap_returns_404_before_first_publication()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/game-config/bootstrap");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertErrorCodeAsync(response, "GAME_CONFIG_NOT_PUBLISHED");
    }

    [Fact]
    public async Task Bootstrap_is_rate_limited_with_standard_error_shape()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        for (var requestIndex = 0; requestIndex < 120; requestIndex++)
        {
            using var allowed = await client.GetAsync("/api/game-config/bootstrap");
            Assert.Equal(HttpStatusCode.NotFound, allowed.StatusCode);
        }

        using var rejected = await client.GetAsync("/api/game-config/bootstrap");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Equal("60", rejected.Headers.RetryAfter?.Delta?.TotalSeconds.ToString() ??
            rejected.Headers.GetValues("Retry-After").Single());
        await AssertErrorCodeAsync(rejected, "RATE_LIMITED");
    }

    [Fact]
    public async Task Published_snapshot_supports_etag_and_atomic_revision_switch()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<GameConfigService>();

        var admin = await service.GetAdminAsync(CancellationToken.None);
        await service.UpsertAvatarAsync(
            new AvatarDefinitionInput(1, "默认头像", "Avatar_Default", 0, true, admin.EditRevision),
            CancellationToken.None);
        admin = await service.GetAdminAsync(CancellationToken.None);
        await service.UpsertCardPackAsync(
            new CardPackDefinitionInput(
                1001,
                "基础卡包",
                "CardPack_Default",
                1000,
                null,
                null,
                0,
                true,
                admin.EditRevision),
            CancellationToken.None);
        admin = await service.GetAdminAsync(CancellationToken.None);
        var publication = await service.PublishAsync(admin.EditRevision, CancellationToken.None);

        var response = await client.GetAsync("/api/game-config/bootstrap");
        response.EnsureSuccessStatusCode();
        Assert.Equal("\"game-config-1\"", response.Headers.ETag?.Tag);
        Assert.Contains("must-revalidate", response.Headers.CacheControl?.ToString());
        Assert.True(response.Headers.Contains("X-Game-Config-Revision"));
        Assert.True(response.Headers.Contains("X-Server-Time"));
        var payload = await response.Content.ReadFromJsonAsync<GameConfigPayload>();
        Assert.NotNull(payload);
        Assert.Equal(publication.PublishedRevision, payload.Revision);
        Assert.Equal(1, payload.SchemaVersion);
        Assert.Single(payload.Avatars);
        Assert.Equal("Avatar_Default", payload.Avatars[0].ResourceKey);
        Assert.Single(payload.CardPacks);
        Assert.Equal(1000, payload.CardPacks[0].PriceGold);

        using var conditional = new HttpRequestMessage(HttpMethod.Get, "/api/game-config/bootstrap");
        conditional.Headers.TryAddWithoutValidation("If-None-Match", "\"game-config-1\"");
        var notModified = await client.SendAsync(conditional);
        Assert.Equal(HttpStatusCode.NotModified, notModified.StatusCode);
        Assert.Empty(await notModified.Content.ReadAsByteArrayAsync());
        Assert.True(notModified.Headers.Contains("X-Server-Time"));

        admin = await service.GetAdminAsync(CancellationToken.None);
        await service.UpsertCardPackAsync(
            new CardPackDefinitionInput(
                1001,
                "基础卡包",
                "CardPack_Default",
                1200,
                null,
                null,
                0,
                true,
                admin.EditRevision),
            CancellationToken.None);

        using var beforePublish = new HttpRequestMessage(HttpMethod.Get, "/api/game-config/bootstrap");
        beforePublish.Headers.TryAddWithoutValidation("If-None-Match", "\"game-config-1\"");
        Assert.Equal(HttpStatusCode.NotModified, (await client.SendAsync(beforePublish)).StatusCode);

        admin = await service.GetAdminAsync(CancellationToken.None);
        await service.PublishAsync(admin.EditRevision, CancellationToken.None);
        var revisionTwo = await client.GetFromJsonAsync<GameConfigPayload>("/api/game-config/bootstrap");
        Assert.NotNull(revisionTwo);
        Assert.Equal(2, revisionTwo.Revision);
        Assert.Equal(1200, revisionTwo.CardPacks[0].PriceGold);
    }

    [Fact]
    public async Task Draft_uses_edit_revision_and_published_items_cannot_be_deleted()
    {
        using var factory = new ApiFactory();
        factory.CreateClient().Dispose();
        await using var scope = factory.Services.CreateAsyncScope();
        var service = scope.ServiceProvider.GetRequiredService<GameConfigService>();

        var admin = await service.GetAdminAsync(CancellationToken.None);
        await service.UpsertAvatarAsync(
            new AvatarDefinitionInput(5, "Avatar", "Avatar_5", 0, true, admin.EditRevision),
            CancellationToken.None);

        var stale = await Assert.ThrowsAsync<ApiException>(() => service.UpsertCardPackAsync(
            new CardPackDefinitionInput(5, "Pack", "Pack_5", 1, null, null, 0, true, admin.EditRevision),
            CancellationToken.None));
        Assert.Equal("GAME_CONFIG_CHANGED", stale.Code);

        admin = await service.GetAdminAsync(CancellationToken.None);
        await service.PublishAsync(admin.EditRevision, CancellationToken.None);
        admin = await service.GetAdminAsync(CancellationToken.None);
        var immutable = await Assert.ThrowsAsync<ApiException>(() =>
            service.DeleteAvatarAsync(5, admin.EditRevision, CancellationToken.None));
        Assert.Equal("PUBLISHED_CONFIG_ITEM_CANNOT_BE_DELETED", immutable.Code);
    }

    [Fact]
    public async Task Admin_page_reuses_publish_login_and_requires_antiforgery_token()
    {
        using var factory = new ApiFactory();
        using var browser = factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

        var loginPage = await browser.GetAsync("/admin/content/login?returnUrl=/admin/game-config");
        string loginHtml = await loginPage.Content.ReadAsStringAsync();
        string token = ExtractAntiforgeryToken(loginHtml);
        var login = await browser.PostAsync("/admin/content/login", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["PublishKey"] = ApiFactory.PublishKey,
            ["ReturnUrl"] = "/admin/game-config",
            ["__RequestVerificationToken"] = token
        }));
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);

        var page = await browser.GetAsync("/admin/game-config");
        string html = await page.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, page.StatusCode);
        Assert.Contains("游戏配置工作台", html, StringComparison.Ordinal);
        Assert.Contains("一键导出当前草稿", html, StringComparison.Ordinal);
        Assert.Contains("Git 配置历史", html, StringComparison.Ordinal);
        Assert.Contains("game-config-admin.js", html, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiFactory.PublishKey, html, StringComparison.Ordinal);

        var export = await browser.GetAsync("/admin/game-config?handler=ExportCsv");
        export.EnsureSuccessStatusCode();
        Assert.Equal("text/csv", export.Content.Headers.ContentType?.MediaType);
        var csv = await export.Content.ReadAsByteArrayAsync();
        Assert.StartsWith("Table,Id,Name,ResourceKey", System.Text.Encoding.UTF8.GetString(csv).TrimStart('\uFEFF'));

        var pageToken = ExtractAntiforgeryToken(html);
        using var importForm = new MultipartFormDataContent();
        importForm.Add(new StringContent("0"), "expectedEditRevision");
        importForm.Add(new StringContent(pageToken), "__RequestVerificationToken");
        importForm.Add(new ByteArrayContent(csv), "csvFile", "game-config.csv");
        var import = await browser.PostAsync("/admin/game-config?handler=ImportCsv", importForm);
        Assert.Equal(HttpStatusCode.Redirect, import.StatusCode);

        var importedPage = await browser.GetAsync("/admin/game-config");
        var importedHtml = await importedPage.Content.ReadAsStringAsync();
        Assert.Contains(
            "CSV 已载入草稿",
            WebUtility.HtmlDecode(importedHtml),
            StringComparison.Ordinal);

        var withoutToken = await browser.PostAsync(
            "/admin/game-config?handler=Publish",
            new FormUrlEncodedContent(new Dictionary<string, string> { ["expectedEditRevision"] = "0" }));
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
        Assert.True(response.Headers.Contains("X-Request-Id"));
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    private sealed record GameConfigPayload(
        int SchemaVersion,
        long Revision,
        AvatarPayload[] Avatars,
        CardPackPayload[] CardPacks);
    private sealed record AvatarPayload(int Id, string ResourceKey, bool IsEnabled);
    private sealed record CardPackPayload(int Id, long PriceGold, bool IsEnabled);
}
