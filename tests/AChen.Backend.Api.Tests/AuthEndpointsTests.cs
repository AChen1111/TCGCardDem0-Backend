using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AChen.Backend.Api.Tests;

public sealed class AuthEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly HttpClient client = factory.CreateClient();

    [Fact]
    public async Task Register_creates_account_and_access_token_can_read_current_user()
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            username = "NewPlayer",
            email = "player@example.com",
            password = "correct-horse-42"
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var auth = await response.Content.ReadFromJsonAsync<AuthPayload>();
        Assert.NotNull(auth);
        Assert.False(string.IsNullOrWhiteSpace(auth.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(auth.RefreshToken));

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var meResponse = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode);
        using var me = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
        Assert.Equal("NewPlayer", me.RootElement.GetProperty("username").GetString());
        Assert.Equal("player@example.com", me.RootElement.GetProperty("email").GetString());
    }

    [Fact]
    public async Task Register_rejects_case_insensitive_duplicate_username_and_email()
    {
        var first = await RegisterAsync("DuplicateOne", "duplicate@example.com");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var duplicateUsername = await RegisterAsync("duplicateone", "other@example.com");
        var duplicateEmail = await RegisterAsync("OtherPlayer", "DUPLICATE@example.com");

        Assert.Equal(HttpStatusCode.Conflict, duplicateUsername.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, duplicateEmail.StatusCode);
        await AssertErrorCodeAsync(duplicateUsername, "ACCOUNT_EXISTS");
        await AssertErrorCodeAsync(duplicateEmail, "ACCOUNT_EXISTS");
    }

    [Fact]
    public async Task Register_rejects_invalid_fields()
    {
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            username = "x!",
            email = "not-an-email",
            password = "short"
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("username", out _));
        Assert.True(errors.TryGetProperty("email", out _));
        Assert.True(errors.TryGetProperty("password", out _));
    }

    [Fact]
    public async Task Login_accepts_username_or_email_and_rejects_wrong_password()
    {
        var register = await RegisterAsync("LoginPlayer", "login@example.com");
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var byUsername = await LoginAsync("loginplayer", "correct-horse-42");
        var byEmail = await LoginAsync("LOGIN@example.com", "correct-horse-42");
        var wrongPassword = await LoginAsync("LoginPlayer", "wrong-password");

        Assert.Equal(HttpStatusCode.OK, byUsername.StatusCode);
        Assert.Equal(HttpStatusCode.OK, byEmail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        await AssertErrorCodeAsync(wrongPassword, "INVALID_CREDENTIALS");
    }

    [Fact]
    public async Task Refresh_rotates_token_and_rejects_reuse()
    {
        var register = await RegisterAsync("RefreshPlayer", "refresh@example.com");
        var original = await register.Content.ReadFromJsonAsync<AuthPayload>();
        Assert.NotNull(original);

        var rotatedResponse = await RefreshAsync(original.RefreshToken);
        Assert.Equal(HttpStatusCode.OK, rotatedResponse.StatusCode);
        var rotated = await rotatedResponse.Content.ReadFromJsonAsync<AuthPayload>();
        Assert.NotNull(rotated);
        Assert.NotEqual(original.RefreshToken, rotated.RefreshToken);

        var reuseResponse = await RefreshAsync(original.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, reuseResponse.StatusCode);
        await AssertErrorCodeAsync(reuseResponse, "INVALID_REFRESH_TOKEN");
    }

    [Fact]
    public async Task Logout_revokes_refresh_token()
    {
        var register = await RegisterAsync("LogoutPlayer", "logout@example.com");
        var auth = await register.Content.ReadFromJsonAsync<AuthPayload>();
        Assert.NotNull(auth);

        var logout = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        var refresh = await RefreshAsync(auth.RefreshToken);
        Assert.Equal(HttpStatusCode.Unauthorized, refresh.StatusCode);
    }

    [Fact]
    public async Task Me_requires_access_token()
    {
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertErrorCodeAsync(response, "INVALID_ACCESS_TOKEN");
    }

    private Task<HttpResponseMessage> RegisterAsync(string username, string email) =>
        client.PostAsJsonAsync("/api/auth/register", new
        {
            username,
            email,
            password = "correct-horse-42"
        });

    private Task<HttpResponseMessage> LoginAsync(string identifier, string password) =>
        client.PostAsJsonAsync("/api/auth/login", new { identifier, password });

    private Task<HttpResponseMessage> RefreshAsync(string refreshToken) =>
        client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken });

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
    }

    private sealed record AuthPayload(string AccessToken, string RefreshToken, int ExpiresInSeconds);
}
