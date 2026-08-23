using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using AChen.Backend.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace AChen.Backend.Api.Tests;

public sealed class AccountRegistrationPageTests
{
    [Fact]
    public async Task Register_page_is_public_and_renders_secure_account_form()
    {
        using var factory = new ApiFactory();
        using var browser = CreateBrowser(factory);

        using var response = await browser.GetAsync("/register");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString(), StringComparison.Ordinal);
        Assert.Contains("创建玩家账号", html, StringComparison.Ordinal);
        Assert.Contains("account.css", html, StringComparison.Ordinal);
        Assert.Contains("account-register.js", html, StringComparison.Ordinal);
        Assert.Contains("name=\"__RequestVerificationToken\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("发布密钥", html, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiFactory.PublishKey, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_page_creates_user_and_profile_without_orphan_refresh_session()
    {
        using var factory = new ApiFactory();
        using var browser = CreateBrowser(factory);
        var page = await browser.GetAsync("/register");
        var token = ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());

        using var response = await browser.PostAsync("/register", Form(
            token,
            username: "WebPlayer",
            email: "WebPlayer@example.com",
            password: "correct-horse-42",
            confirmation: "correct-horse-42"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/register", response.Headers.Location?.OriginalString);

        using var completed = await browser.GetAsync(response.Headers.Location);
        var completedHtml = await completed.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, completed.StatusCode);
        Assert.Contains("账号创建完成", completedHtml, StringComparison.Ordinal);
        Assert.Contains("WebPlayer", completedHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("correct-horse-42", completedHtml, StringComparison.Ordinal);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync();
        var profile = await db.PlayerProfiles.SingleAsync();
        Assert.Equal("WebPlayer", user.Username);
        Assert.Equal("webplayer@example.com", user.Email);
        Assert.Equal(user.Id, profile.UserId);
        Assert.Equal("WebPlayer", profile.Nickname);
        Assert.Empty(await db.RefreshSessions.ToListAsync());
    }

    [Fact]
    public async Task Register_page_shows_validation_without_reflecting_passwords()
    {
        using var factory = new ApiFactory();
        using var browser = CreateBrowser(factory);
        var page = await browser.GetAsync("/register");
        var token = ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());

        using var response = await browser.PostAsync("/register", Form(
            token,
            username: "x!",
            email: "not-an-email",
            password: "secret-should-never-return-42",
            confirmation: "different-secret-42"));
        var html = WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("用户名需为 3–24 位", html, StringComparison.Ordinal);
        Assert.Contains("请输入有效的邮箱地址", html, StringComparison.Ordinal);
        Assert.Contains("两次输入的密码不一致", html, StringComparison.Ordinal);
        Assert.DoesNotContain("secret-should-never-return-42", html, StringComparison.Ordinal);
        Assert.DoesNotContain("different-secret-42", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Register_page_rejects_duplicate_account_and_missing_antiforgery_token()
    {
        using var factory = new ApiFactory();
        using var browser = CreateBrowser(factory);
        using var existing = await browser.PostAsJsonAsync("/api/auth/register", new
        {
            username = "ExistingPlayer",
            email = "existing@example.com",
            password = "correct-horse-42"
        });
        Assert.Equal(HttpStatusCode.Created, existing.StatusCode);

        var page = await browser.GetAsync("/register");
        var token = ExtractAntiforgeryToken(await page.Content.ReadAsStringAsync());
        using var duplicate = await browser.PostAsync("/register", Form(
            token,
            username: "existingplayer",
            email: "other@example.com",
            password: "another-password-42",
            confirmation: "another-password-42"));
        var duplicateHtml = WebUtility.HtmlDecode(await duplicate.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Contains("该用户名或邮箱已被注册", duplicateHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("another-password-42", duplicateHtml, StringComparison.Ordinal);

        using var withoutToken = await browser.PostAsync("/register", Form(
            token: null,
            username: "NoCsrfPlayer",
            email: "no-csrf@example.com",
            password: "correct-horse-42",
            confirmation: "correct-horse-42"));
        Assert.Equal(HttpStatusCode.BadRequest, withoutToken.StatusCode);
    }

    private static HttpClient CreateBrowser(ApiFactory factory) => factory.CreateClient(
        new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static FormUrlEncodedContent Form(
        string? token,
        string username,
        string email,
        string password,
        string confirmation)
    {
        var values = new Dictionary<string, string>
        {
            ["Input.Username"] = username,
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.ConfirmPassword"] = confirmation
        };
        if (token is not null)
        {
            values["__RequestVerificationToken"] = token;
        }

        return new FormUrlEncodedContent(values);
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var match = Regex.Match(
            html,
            "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"",
            RegexOptions.CultureInvariant);
        Assert.True(match.Success, "The registration form did not render an antiforgery token.");
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }
}
