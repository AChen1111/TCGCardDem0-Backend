using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace AChen.Backend.Api.Features.ContentDelivery;

public static class ContentPublisherAuthentication
{
    public const string Scheme = "ContentPublishKey";
    public const string Policy = "ContentPublisher";
    public const string HeaderName = "X-Content-Publish-Key";
    public const string FingerprintClaim = "content_publish_key_fingerprint";
}

public sealed class ContentPublisherCredentials(IOptionsMonitor<ContentDeliveryOptions> options)
{
    public bool Validate(string? provided)
    {
        if (string.IsNullOrEmpty(provided))
        {
            return false;
        }

        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(options.CurrentValue.PublishKey));
        var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(provided));
        return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
    }

    public string GetFingerprint() => GetFingerprint(options.CurrentValue.PublishKey);

    private static string GetFingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}

public sealed class PublishKeyAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    ContentPublisherCredentials credentials)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var values = Request.Headers[ContentPublisherAuthentication.HeaderName];
        if (values.Count != 1 || !credentials.Validate(values[0]))
        {
            return Task.FromResult(AuthenticateResult.Fail("需要有效的内容发布密钥"));
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "content-publisher"),
            new Claim(ClaimTypes.Name, "Content publisher"),
            new Claim(ContentPublisherAuthentication.FingerprintClaim, credentials.GetFingerprint())
        };
        var identity = new ClaimsIdentity(claims, ContentPublisherAuthentication.Scheme);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(
                new ClaimsPrincipal(identity),
                ContentPublisherAuthentication.Scheme)));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/problem+json";
        await Response.WriteAsJsonAsync(new
        {
            title = "内容发布密钥无效",
            status = StatusCodes.Status401Unauthorized,
            code = "INVALID_CONTENT_PUBLISH_KEY",
            traceId = Context.TraceIdentifier
        }, Context.RequestAborted);
    }
}
