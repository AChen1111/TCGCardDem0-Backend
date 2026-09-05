using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.Players;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.AccountManagement;

public sealed class AccountManagementService(
    AppDbContext db,
    TimeProvider timeProvider,
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

    public async Task<ManagedAccountDetails?> GetAsync(
        Guid accountId,
        CancellationToken cancellationToken)
    {
        var account = await db.Users
            .AsNoTracking()
            .Include(value => value.PlayerProfile)
            .SingleOrDefaultAsync(value => value.Id == accountId, cancellationToken);
        if (account is null)
        {
            return null;
        }

        var profile = account.PlayerProfile ?? PlayerProfile.ForNewAccount(
            account.Id,
            account.Username,
            account.CreatedAt);
        return new ManagedAccountDetails(
            account.Id,
            account.Username,
            profile.Nickname,
            profile.AvatarId,
            profile.OwnedAvatarIds.ToArray(),
            profile.BackgroundId,
            profile.Gold,
            profile.Revision,
            profile.CreatedAt,
            profile.UpdatedAt);
    }

    public async Task<ManagedAccountUpdateResult> UpdatePlayerDataAsync(
        Guid accountId,
        UpdateManagedPlayerData request,
        CancellationToken cancellationToken)
    {
        var errors = Validate(request);
        if (errors.Count > 0)
        {
            return new ManagedAccountUpdateResult(true, errors);
        }

        var account = await db.Users
            .Include(value => value.PlayerProfile)
            .SingleOrDefaultAsync(value => value.Id == accountId, cancellationToken);
        if (account is null)
        {
            logger.LogWarning("Player data update failed because account {AccountId} was not found.", accountId);
            return new ManagedAccountUpdateResult(false, errors);
        }

        var now = timeProvider.GetUtcNow();
        var ownedAvatarIds = request.OwnedAvatarIds.Distinct().OrderBy(value => value).ToList();
        var profile = account.PlayerProfile;
        if (profile is null)
        {
            profile = new PlayerProfile
            {
                UserId = account.Id,
                Nickname = request.Nickname.Trim(),
                AvatarId = request.AvatarId,
                OwnedAvatarIds = ownedAvatarIds,
                BackgroundId = request.BackgroundId,
                Gold = request.Gold,
                Revision = 1,
                CreatedAt = now,
                UpdatedAt = now
            };
            db.PlayerProfiles.Add(profile);
        }
        else
        {
            profile.Nickname = request.Nickname.Trim();
            profile.AvatarId = request.AvatarId;
            profile.OwnedAvatarIds = ownedAvatarIds;
            profile.BackgroundId = request.BackgroundId;
            profile.Gold = request.Gold;
            profile.Revision++;
            profile.UpdatedAt = now;
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogWarning(
                "Player data update conflicted with another update for account {AccountId}.",
                accountId);
            return new ManagedAccountUpdateResult(true, new Dictionary<string, string[]>
            {
                [""] = ["玩家数据已在其他位置发生变化，请刷新页面后重试。"]
            });
        }

        logger.LogInformation(
            "Updated all editable player data for account {AccountId} at revision {Revision}.",
            accountId,
            profile.Revision);
        return new ManagedAccountUpdateResult(true, errors);
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

    private static Dictionary<string, string[]> Validate(UpdateManagedPlayerData request)
    {
        var errors = new Dictionary<string, string[]>();
        var nickname = request.Nickname?.Trim() ?? "";
        if (nickname.Length is < 2 or > 24 || nickname.Any(char.IsControl))
        {
            errors["Nickname"] = ["昵称必须为 2–24 个字符，且不能包含控制字符。"];
        }

        if (request.AvatarId is < 0)
        {
            errors["AvatarId"] = ["当前头像 ID 不能为负数。"];
        }

        if (request.OwnedAvatarIds.Count > 256 || request.OwnedAvatarIds.Any(value => value < 0))
        {
            errors["OwnedAvatarIds"] = ["已拥有头像最多 256 个，且 ID 不能为负数。"];
        }
        else if (request.AvatarId is int avatarId && !request.OwnedAvatarIds.Contains(avatarId))
        {
            errors["OwnedAvatarIds"] = ["已拥有头像列表必须包含当前头像 ID。"];
        }

        if (request.BackgroundId is <= 0)
        {
            errors["BackgroundId"] = ["背景 ID 必须大于 0，或留空。"];
        }

        if (request.Gold < 0)
        {
            errors["Gold"] = ["金币不能为负数。"];
        }

        return errors;
    }
}
