using System.Text;
using System.Threading.RateLimiting;
using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.Auth;
using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Features.GameConfig;
using AChen.Backend.Api.Features.Players;
using AChen.Backend.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
var maxArchiveBytes = builder.Configuration.GetValue<long?>("ContentDelivery:MaxArchiveBytes")
    ?? 2L * 1024 * 1024 * 1024;
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = maxArchiveBytes);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddRazorPages();
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = maxArchiveBytes);
builder.Services.AddDbContext<AppDbContext>((services, options) =>
    options.UseSqlite(DatabaseConfiguration.GetSqliteConnectionString(
        services.GetRequiredService<IConfiguration>(),
        services.GetRequiredService<IHostEnvironment>())));
builder.Services.AddOptions<AuthOptions>()
    .BindConfiguration(AuthOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.Issuer), "Auth:Issuer is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Audience), "Auth:Audience is required.")
    .Validate(options => options.SigningKey.Length >= 32, "Auth:SigningKey must contain at least 32 characters.")
    .Validate(options => options.AccessTokenMinutes is >= 1 and <= 60, "Auth:AccessTokenMinutes must be between 1 and 60.")
    .Validate(options => options.RefreshTokenDays is >= 1 and <= 30, "Auth:RefreshTokenDays must be between 1 and 30.")
    .ValidateOnStart();
builder.Services.AddOptions<ContentDeliveryOptions>()
    .BindConfiguration(ContentDeliveryOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.StorageRoot), "ContentDelivery:StorageRoot is required.")
    .Validate(options => options.PublishKey.Length >= 32, "ContentDelivery:PublishKey must contain at least 32 characters.")
    .Validate(options => options.MaxArchiveBytes > 0, "ContentDelivery:MaxArchiveBytes must be positive.")
    .Validate(options => options.MaxExpandedBytes >= options.MaxArchiveBytes, "ContentDelivery:MaxExpandedBytes must be at least MaxArchiveBytes.")
    .Validate(options => options.MaxFileCount is >= 1 and <= 100_000, "ContentDelivery:MaxFileCount must be between 1 and 100000.")
    .Validate(options => options.AllowedChannels.Length > 0 && options.AllowedChannels.All(value => !string.IsNullOrWhiteSpace(value)), "ContentDelivery:AllowedChannels must not be empty.")
    .ValidateOnStart();
builder.Services.AddOptions<GameConfigGitOptions>()
    .BindConfiguration(GameConfigGitOptions.SectionName)
    .Validate(options => !string.IsNullOrWhiteSpace(options.RepositoryRoot), "GameConfigGit:RepositoryRoot is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.Branch), "GameConfigGit:Branch is required.")
    .Validate(options => !string.IsNullOrWhiteSpace(options.RemoteName), "GameConfigGit:RemoteName is required.")
    .Validate(options => options.HistoryLimit is >= 1 and <= 100, "GameConfigGit:HistoryLimit must be between 1 and 100.")
    .ValidateOnStart();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<IPlayerRepository, PlayerRepository>();
builder.Services.AddScoped<PlayerService>();
builder.Services.AddScoped<IGameConfigRepository, GameConfigRepository>();
builder.Services.AddScoped<GameConfigService>();
builder.Services.AddSingleton<GameConfigCsvSerializer>();
builder.Services.AddScoped<GameConfigGitService>();
builder.Services.AddScoped<IContentReleaseRepository, ContentReleaseRepository>();
builder.Services.AddScoped<ContentReleaseService>();
builder.Services.AddSingleton<IContentStorage, LocalContentStorage>();
builder.Services.AddSingleton<ContentReleaseLockProvider>();
builder.Services.AddSingleton<ContentPublisherCredentials>();
builder.Services.AddHostedService<ContentStagingCleanupService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var auth = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>()!;
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = auth.Issuer,
            ValidateAudience = true,
            ValidAudience = auth.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30)
        };
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";
                await context.Response.WriteAsJsonAsync(new
                {
                    title = "Authentication is required.",
                    status = StatusCodes.Status401Unauthorized,
                    code = "INVALID_ACCESS_TOKEN",
                    traceId = context.HttpContext.TraceIdentifier
                }, context.HttpContext.RequestAborted);
            }
        };
    })
    .AddScheme<AuthenticationSchemeOptions, PublishKeyAuthenticationHandler>(
        ContentPublisherAuthentication.Scheme,
        _ => { })
    .AddCookie(ContentAdminAuthentication.Scheme, options =>
    {
        options.LoginPath = "/admin/content/login";
        options.AccessDeniedPath = "/admin/content/login";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = false;
        options.Cookie.Name = "AChen.ContentAdmin";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
            ? CookieSecurePolicy.SameAsRequest
            : CookieSecurePolicy.Always;
        options.Events = new CookieAuthenticationEvents
        {
            OnValidatePrincipal = async context =>
            {
                var credentials = context.HttpContext.RequestServices
                    .GetRequiredService<ContentPublisherCredentials>();
                var fingerprint = context.Principal?.FindFirst(
                    ContentPublisherAuthentication.FingerprintClaim)?.Value;
                if (!string.Equals(fingerprint, credentials.GetFingerprint(), StringComparison.Ordinal))
                {
                    context.RejectPrincipal();
                    await context.HttpContext.SignOutAsync(ContentAdminAuthentication.Scheme);
                }
            }
        };
    });
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy(ContentPublisherAuthentication.Policy, policy =>
    {
        policy.AddAuthenticationSchemes(ContentPublisherAuthentication.Scheme);
        policy.RequireAuthenticatedUser();
    });
});
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("content-management", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 60,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("content-manifest", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("player", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("game-config", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 120,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("content-upload", context => RateLimitPartition.GetConcurrencyLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = 1,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst
        }));
    options.AddPolicy("admin-login", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            title = "Too many requests.",
            status = StatusCodes.Status429TooManyRequests,
            code = "RATE_LIMITED",
            traceId = context.HttpContext.TraceIdentifier
        }, cancellationToken);
    };
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;
    await database.MigrateAsync();
}

app.Use(async (context, next) =>
{
    context.Response.OnStarting(() =>
    {
        context.Response.Headers["X-Request-Id"] = context.TraceIdentifier;
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; style-src 'self'; img-src 'self'; form-action 'self'; frame-ancestors 'none'; base-uri 'self'";
        return Task.CompletedTask;
    });
    await next();
});
app.UseExceptionHandler();
app.UseStaticFiles();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/ready", async (AppDbContext db, CancellationToken cancellationToken) =>
    await db.Database.CanConnectAsync(cancellationToken) &&
    await app.Services.GetRequiredService<IContentStorage>().CheckReadyAsync(cancellationToken)
        ? Results.Ok(new { status = "ready" })
        : Results.StatusCode(StatusCodes.Status503ServiceUnavailable));
app.MapAuthEndpoints();
app.MapPlayerEndpoints();
app.MapGameConfigEndpoints();
app.MapContentEndpoints();
app.MapRazorPages();

app.Run();

public partial class Program;
