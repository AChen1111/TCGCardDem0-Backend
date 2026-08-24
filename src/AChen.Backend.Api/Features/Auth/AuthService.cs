using AChen.Backend.Api.Data;
using AChen.Backend.Api.Infrastructure;
using AChen.Backend.Api.Features.Players;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.Auth;

public sealed class AuthService(
    AppDbContext db,
    IPasswordHasher<User> passwordHasher,
    TokenService tokenService,
    PlayerService playerService,
    TimeProvider timeProvider)
{
    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var user = await AddAccountAsync(request, cancellationToken);
        var issued = tokenService.Issue(user);
        user.RefreshSessions.Add(issued.RefreshSession);
        await SaveAccountAsync(cancellationToken);
        return await ToAuthResponseAsync(user, issued, cancellationToken);
    }

    public async Task<UserResponse> CreateAccountAsync(
        RegisterRequest request,
        CancellationToken cancellationToken)
    {
        var user = await AddAccountAsync(request, cancellationToken);
        await SaveAccountAsync(cancellationToken);
        return ToUserResponse(user);
    }

    private async Task<User> AddAccountAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var email = request.Email.Trim().ToLowerInvariant();
        var normalizedUsername = username.ToUpperInvariant();
        var normalizedEmail = email;

        var accountExists = await db.Users.AnyAsync(
            user => user.NormalizedUsername == normalizedUsername || user.NormalizedEmail == normalizedEmail,
            cancellationToken);
        if (accountExists)
        {
            throw new ApiException(StatusCodes.Status409Conflict, "ACCOUNT_EXISTS", "Username or email is already registered.");
        }

        var now = timeProvider.GetUtcNow();
        var user = new User
        {
            Username = username,
            NormalizedUsername = normalizedUsername,
            Email = email,
            NormalizedEmail = normalizedEmail,
            PasswordHash = "",
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        db.Users.Add(user);
        db.PlayerProfiles.Add(new PlayerProfile
        {
            UserId = user.Id,
            Nickname = username,
            Gold = 0,
            Revision = 0,
            CreatedAt = now,
            UpdatedAt = now
        });

        return user;
    }

    private async Task SaveAccountAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (exception.InnerException is SqliteException { SqliteErrorCode: 19 })
        {
            throw new ApiException(StatusCodes.Status409Conflict, "ACCOUNT_EXISTS", "Username or email is already registered.");
        }
    }

    public async Task<AuthResponse> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var identifier = request.Identifier.Trim();
        var normalizedUsername = identifier.ToUpperInvariant();
        var normalizedEmail = identifier.ToLowerInvariant();
        var user = await db.Users.SingleOrDefaultAsync(
            value => value.NormalizedUsername == normalizedUsername || value.NormalizedEmail == normalizedEmail,
            cancellationToken);

        if (user is null)
        {
            throw new ApiException(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "Identifier or password is incorrect.");
        }

        var verification = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (verification == PasswordVerificationResult.Failed)
        {
            throw new ApiException(StatusCodes.Status401Unauthorized, "INVALID_CREDENTIALS", "Identifier or password is incorrect.");
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = passwordHasher.HashPassword(user, request.Password);
            user.UpdatedAt = timeProvider.GetUtcNow();
        }

        var issued = tokenService.Issue(user);
        db.RefreshSessions.Add(issued.RefreshSession);
        await db.SaveChangesAsync(cancellationToken);
        return await ToAuthResponseAsync(user, issued, cancellationToken);
    }

    public async Task<AuthResponse> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = TokenService.HashRefreshToken(refreshToken);
        var session = await db.RefreshSessions
            .Include(value => value.User)
            .SingleOrDefaultAsync(value => value.TokenHash == tokenHash, cancellationToken);
        var now = timeProvider.GetUtcNow();

        if (session is null || session.RevokedAt is not null || session.ExpiresAt <= now)
        {
            throw InvalidRefreshToken();
        }

        var issued = tokenService.Issue(session.User);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var updatedRows = await db.RefreshSessions
            .Where(value => value.Id == session.Id && value.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.RevokedAt, now)
                .SetProperty(value => value.ReplacedByTokenHash, issued.RefreshSession.TokenHash),
                cancellationToken);
        if (updatedRows != 1)
        {
            throw InvalidRefreshToken();
        }

        db.RefreshSessions.Add(issued.RefreshSession);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return await ToAuthResponseAsync(session.User, issued, cancellationToken);
    }

    public async Task LogoutAsync(string refreshToken, CancellationToken cancellationToken)
    {
        var tokenHash = TokenService.HashRefreshToken(refreshToken);
        var now = timeProvider.GetUtcNow();
        await db.RefreshSessions
            .Where(value => value.TokenHash == tokenHash && value.RevokedAt == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(value => value.RevokedAt, now), cancellationToken);
    }

    public async Task<UserResponse> GetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(value => value.Id == userId, cancellationToken);
        if (user is null)
        {
            throw new ApiException(StatusCodes.Status401Unauthorized, "INVALID_ACCESS_TOKEN", "Access token is no longer valid.");
        }

        return ToUserResponse(user);
    }

    private async Task<AuthResponse> ToAuthResponseAsync(
        User user,
        IssuedTokens issued,
        CancellationToken cancellationToken) => new(
            issued.AccessToken,
            issued.RefreshToken,
            issued.ExpiresInSeconds,
            ToUserResponse(user),
            await playerService.GetAsync(user.Id, cancellationToken));

    private static UserResponse ToUserResponse(User user) =>
        new(user.Id, user.Username, user.Email, user.CreatedAt);

    private static ApiException InvalidRefreshToken() =>
        new(StatusCodes.Status401Unauthorized, "INVALID_REFRESH_TOKEN", "Refresh token is invalid or expired.");
}
