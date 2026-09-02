using AChen.Backend.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.AccountManagement;

public sealed class AccountManagementService(
    AppDbContext db,
    ILogger<AccountManagementService> logger)
{
    public async Task<ManagedAccountPage> ListAsync(
        int page,
        int pageSize,
        string? search,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var normalizedSearch = search?.Trim().ToUpperInvariant();

        var query = db.Users.AsNoTracking();
        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            query = query.Where(account =>
                account.NormalizedUsername.Contains(normalizedSearch));
        }

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = Math.Max(1, (int)Math.Ceiling(totalItems / (double)pageSize));
        page = Math.Min(page, totalPages);

        var items = await query
            .OrderBy(account => account.NormalizedUsername)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(account => new ManagedAccountSummary(
                account.Id,
                account.Username,
                account.CreatedAt,
                account.UpdatedAt,
                account.PlayerProfile == null ? null : account.PlayerProfile.Nickname,
                account.PlayerProfile == null ? null : account.PlayerProfile.Gold,
                account.RefreshSessions.Count))
            .ToListAsync(cancellationToken);

        return new ManagedAccountPage(items, page, pageSize, totalItems, totalPages);
    }

    public async Task<bool> DeleteAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await db.Users.SingleOrDefaultAsync(
            value => value.Id == accountId,
            cancellationToken);
        if (account is null)
        {
            logger.LogWarning("Account deletion failed because account {AccountId} was not found.", accountId);
            return false;
        }

        var username = account.Username;
        db.Users.Remove(account);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Deleted account {AccountId} ({Username}) and its related player data and refresh sessions.",
            accountId,
            username);
        return true;
    }
}
