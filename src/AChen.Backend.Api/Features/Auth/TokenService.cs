using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace AChen.Backend.Api.Features.Auth;

public sealed class TokenService(IOptions<AuthOptions> options, TimeProvider timeProvider)
{
    private readonly AuthOptions auth = options.Value;

    public IssuedTokens Issue(User user)
    {
        var now = timeProvider.GetUtcNow();
        var accessExpiresAt = now.AddMinutes(auth.AccessTokenMinutes);
        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.SigningKey)),
            SecurityAlgorithms.HmacSha256);
        var jwt = new JwtSecurityToken(
            issuer: auth.Issuer,
            audience: auth.Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
                new Claim(JwtRegisteredClaimNames.UniqueName, user.Username),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
            ],
            notBefore: now.UtcDateTime,
            expires: accessExpiresAt.UtcDateTime,
            signingCredentials: credentials);

        var refreshToken = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var session = new RefreshSession
        {
            UserId = user.Id,
            User = user,
            TokenHash = HashRefreshToken(refreshToken),
            CreatedAt = now,
            ExpiresAt = now.AddDays(auth.RefreshTokenDays)
        };

        return new IssuedTokens(
            new JwtSecurityTokenHandler().WriteToken(jwt),
            refreshToken,
            checked(auth.AccessTokenMinutes * 60),
            session);
    }

    public static string HashRefreshToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}

public sealed record IssuedTokens(
    string AccessToken,
    string RefreshToken,
    int ExpiresInSeconds,
    RefreshSession RefreshSession);
