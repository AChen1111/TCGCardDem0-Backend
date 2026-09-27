using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using AChen.Backend.Api.Data;
using AChen.Backend.Api.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.Decks;

public static class DeckEndpoints
{
    public static IEndpointRouteBuilder MapDeckEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/player/decks").RequireAuthorization().RequireRateLimiting("player")
            .AddEndpointFilter(async (context, next) =>
            {
                context.HttpContext.Response.Headers.CacheControl = "no-store";
                return await next(context);
            });
        group.MapGet("", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        group.MapPost("", CreateAsync).WithMetadata(new RequestSizeLimitAttribute(16 * 1024));
        group.MapPut("/{id:guid}", SaveAsync).WithMetadata(new RequestSizeLimitAttribute(16 * 1024));
        group.MapDelete("/{id:guid}", DeleteAsync);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(HttpContext context, AppDbContext db, CancellationToken ct)
    {
        Guid userId = UserId(context);
        var decks = await db.PlayerDecks.AsNoTracking().Where(x => x.UserId == userId)
            .OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).ToListAsync(ct);
        return Results.Ok(decks.Select(ToResponse).ToArray());
    }

    private static async Task<IResult> GetAsync(Guid id, HttpContext context, AppDbContext db, CancellationToken ct) =>
        Results.Ok(ToResponse(await FindAsync(id, UserId(context), db, ct)));

    private static async Task<IResult> CreateAsync(CreateDeckRequest request, HttpContext context,
        AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        ValidateName(request.Name);
        var now = clock.GetUtcNow();
        var deck = new PlayerDeck { UserId = UserId(context), Name = request.Name.Trim(), CreatedAt = now, UpdatedAt = now };
        db.PlayerDecks.Add(deck);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/api/player/decks/{deck.Id}", ToResponse(deck));
    }

    private static async Task<IResult> SaveAsync(Guid id, SaveDeckRequest request, HttpContext context,
        AppDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var deck = await FindAsync(id, UserId(context), db, ct);
        ValidateName(request.Name);
        ValidateEntries(request.MainDeck);
        ValidateEntries(request.ExtraDeck);
        if (!request.ExpectedRevision.HasValue || request.ExpectedRevision < 0) throw Invalid("缺少有效的 expectedRevision");
        if (deck.Revision != request.ExpectedRevision) throw Changed();
        deck.Name = request.Name.Trim();
        deck.MainDeckJson = JsonSerializer.Serialize(request.MainDeck);
        deck.ExtraDeckJson = JsonSerializer.Serialize(request.ExtraDeck);
        deck.Revision++;
        deck.UpdatedAt = clock.GetUtcNow();
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Changed(); }
        return Results.Ok(ToResponse(deck));
    }

    private static async Task<IResult> DeleteAsync(Guid id, long expectedRevision, HttpContext context,
        AppDbContext db, CancellationToken ct)
    {
        var deck = await FindAsync(id, UserId(context), db, ct);
        if (expectedRevision < 0) throw Invalid("expectedRevision 不能为负数");
        if (deck.Revision != expectedRevision) throw Changed();
        db.PlayerDecks.Remove(deck);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw Changed(); }
        return Results.NoContent();
    }

    private static ApiException Changed() => new(409, "DECK_DATA_CHANGED", "卡组已发生变更，请重新读取后编辑");

    // Transport/storage integrity only. Deliberately independent of card catalogs, inventory and deck rules.
    private static void ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Trim().Length > 64 || name.Any(char.IsControl))
            throw Invalid("卡组名称需为 1-64 个字符且不能包含控制字符");
    }

    private static void ValidateEntries(DeckCardEntry[]? entries)
    {
        if (entries is null || entries.Any(x => x is null || string.IsNullOrWhiteSpace(x.CardId)
            || x.CardId.Length > 128 || x.CardId.Any(char.IsControl) || x.Rarity < 0 || x.Count <= 0))
            throw Invalid("卡组条目结构无效");
    }

    private static ApiException Invalid(string message) => new(422, "VALIDATION_ERROR", message);

    private static async Task<PlayerDeck> FindAsync(Guid id, Guid userId, AppDbContext db, CancellationToken ct) =>
        await db.PlayerDecks.SingleOrDefaultAsync(x => x.Id == id && x.UserId == userId, ct)
        ?? throw new ApiException(404, "DECK_NOT_FOUND", "卡组不存在");

    private static Guid UserId(HttpContext context) =>
        Guid.TryParse(context.User.FindFirst(JwtRegisteredClaimNames.Sub)?.Value, out var id)
            ? id : throw new ApiException(401, "INVALID_ACCESS_TOKEN", "登录状态已失效，请重新登录");

    private static DeckResponse ToResponse(PlayerDeck deck) => new(deck.Id, deck.Name,
        JsonSerializer.Deserialize<DeckCardEntry[]>(deck.MainDeckJson)!,
        JsonSerializer.Deserialize<DeckCardEntry[]>(deck.ExtraDeckJson)!,
        deck.Revision, deck.CreatedAt, deck.UpdatedAt);
}
