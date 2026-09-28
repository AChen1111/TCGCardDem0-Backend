using AChen.Configuration;
using AChen.Backend.Api.Features.ContentDelivery;
using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.Players;
using AChen.Backend.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.Social;

public sealed class SocialService(
    AppDbContext db,
    PublishedConfigReader configReader,
    IPlayerRepository players,
    TimeProvider timeProvider,
    ILogger<SocialService> logger)
{
    private const int SearchLimit = 30;

    public async Task<IReadOnlyList<FriendSummary>> ListFriendsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await EnsurePlayerAsync(userId, cancellationToken);
        var friendIds = await LoadFriendIdsAsync(userId, cancellationToken);
        if (friendIds.Count == 0)
        {
            return [];
        }

        var profiles = await db.PlayerProfiles
            .AsNoTracking()
            .Where(profile => friendIds.Contains(profile.UserId))
            .OrderBy(profile => profile.Nickname)
            .Select(profile => new FriendSummary(profile.UserId, profile.Nickname, profile.AvatarId, profile.AvatarFrameId))
            .ToListAsync(cancellationToken);
        return profiles;
    }

    public async Task<IReadOnlyList<FriendSearchHit>> SearchPlayersAsync(
        Guid userId,
        string? nickname,
        CancellationToken cancellationToken)
    {
        await EnsurePlayerAsync(userId, cancellationToken);
        var needle = nickname?.Trim() ?? string.Empty;
        if (needle.Length == 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_NICKNAME", "搜索昵称不能为空");
        }

        var pattern = "%" + EscapeLike(needle) + "%";
        var hits = await db.PlayerProfiles
            .AsNoTracking()
            .Where(profile =>
                profile.UserId != userId &&
                EF.Functions.Like(profile.Nickname, pattern, "\\"))
            .OrderBy(profile => profile.Nickname)
            .Take(SearchLimit)
            .Select(profile => new { profile.UserId, profile.Nickname, profile.AvatarId, profile.AvatarFrameId })
            .ToListAsync(cancellationToken);

        var friendIds = await LoadFriendIdsAsync(userId, cancellationToken);
        var pendingIds = await LoadOutgoingPendingIdsAsync(userId, cancellationToken);
        return hits
            .Select(hit => new FriendSearchHit(
                hit.UserId,
                hit.Nickname,
                hit.AvatarId,
                friendIds.Contains(hit.UserId),
                pendingIds.Contains(hit.UserId), hit.AvatarFrameId))
            .ToList();
    }

    public async Task<FriendRequestCreated> CreateRequestAsync(
        Guid userId,
        CreateFriendRequestBody request,
        CancellationToken cancellationToken)
    {
        await EnsurePlayerAsync(userId, cancellationToken);
        if (request.TargetPlayerId == Guid.Empty)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "FRIEND_NOT_FOUND", "未找到该玩家");
        }

        if (request.TargetPlayerId == userId)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "FRIEND_SELF", "不能添加自己为好友");
        }

        var target = await db.PlayerProfiles
            .AsNoTracking()
            .SingleOrDefaultAsync(profile => profile.UserId == request.TargetPlayerId, cancellationToken);
        if (target is null)
        {
            throw new ApiException(StatusCodes.Status404NotFound, "FRIEND_NOT_FOUND", "未找到该玩家");
        }

        if (await AreFriendsAsync(userId, request.TargetPlayerId, cancellationToken))
        {
            throw new ApiException(StatusCodes.Status409Conflict, "FRIEND_ALREADY", "已经是好友");
        }

        if (await HasPendingRequestAsync(userId, request.TargetPlayerId, cancellationToken))
        {
            throw new ApiException(StatusCodes.Status409Conflict, "FRIEND_PENDING", "已有待处理的好友申请");
        }

        var now = timeProvider.GetUtcNow();
        var entity = new FriendRequest
        {
            Id = Guid.NewGuid(),
            FromUserId = userId,
            ToUserId = request.TargetPlayerId,
            Status = FriendRequestStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now
        };
        db.FriendRequests.Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Player {UserId} sent friend request {RequestId} to {TargetUserId}.",
            userId,
            entity.Id,
            request.TargetPlayerId);
        return new FriendRequestCreated(entity.Id, request.TargetPlayerId);
    }

    public async Task<object> AcceptRequestAsync(
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var entity = await db.FriendRequests.SingleOrDefaultAsync(
            value => value.Id == requestId,
            cancellationToken);
        if (entity is null || entity.ToUserId != userId || entity.Status != FriendRequestStatus.Pending)
        {
            throw new ApiException(StatusCodes.Status404NotFound, "FRIEND_ACCEPT_FAILED", "无法同意该好友申请");
        }

        var now = timeProvider.GetUtcNow();
        entity.Status = FriendRequestStatus.Accepted;
        entity.UpdatedAt = now;
        await EnsureFriendshipAsync(entity.FromUserId, entity.ToUserId, now, cancellationToken);

        var extras = await db.FriendRequests
            .Where(value =>
                value.Id != entity.Id &&
                value.Status == FriendRequestStatus.Pending &&
                ((value.FromUserId == entity.FromUserId && value.ToUserId == entity.ToUserId) ||
                 (value.FromUserId == entity.ToUserId && value.ToUserId == entity.FromUserId)))
            .ToListAsync(cancellationToken);
        foreach (var extra in extras)
        {
            extra.Status = FriendRequestStatus.Accepted;
            extra.UpdatedAt = now;
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Player {UserId} accepted friend request {RequestId} from {FromUserId}.",
            userId,
            requestId,
            entity.FromUserId);
        return new { id = entity.Id };
    }

    public async Task<object> RejectRequestAsync(
        Guid userId,
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var entity = await db.FriendRequests.SingleOrDefaultAsync(
            value => value.Id == requestId,
            cancellationToken);
        if (entity is null || entity.ToUserId != userId || entity.Status != FriendRequestStatus.Pending)
        {
            throw new ApiException(StatusCodes.Status404NotFound, "FRIEND_REJECT_FAILED", "无法拒绝该好友申请");
        }

        entity.Status = FriendRequestStatus.Rejected;
        entity.UpdatedAt = timeProvider.GetUtcNow();
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Player {UserId} rejected friend request {RequestId} from {FromUserId}.",
            userId,
            requestId,
            entity.FromUserId);
        return new { id = entity.Id };
    }

    public async Task<IReadOnlyList<InboxItem>> GetInboxAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        await EnsurePlayerAsync(userId, cancellationToken);
        var requests = await db.FriendRequests
            .AsNoTracking()
            .Where(value => value.ToUserId == userId && value.Status == FriendRequestStatus.Pending)
            .ToListAsync(cancellationToken);
        var gifts = await db.Gifts
            .AsNoTracking()
            .Where(value => value.TargetUserId == userId && !value.Claimed)
            .ToListAsync(cancellationToken);

        var relatedIds = requests.Select(value => value.FromUserId).Distinct().ToArray();
        var profiles = relatedIds.Length == 0
            ? new Dictionary<Guid, PlayerProfile>()
            : await db.PlayerProfiles
                .AsNoTracking()
                .Where(profile => relatedIds.Contains(profile.UserId))
                .ToDictionaryAsync(profile => profile.UserId, cancellationToken);

        var items = new List<InboxItem>(requests.Count + gifts.Count);
        foreach (var request in requests)
        {
            profiles.TryGetValue(request.FromUserId, out var from);
            items.Add(new InboxItem(
                InboxKinds.FriendRequest,
                request.Id,
                request.CreatedAt,
                request.FromUserId,
                from?.Nickname,
                from?.AvatarId,
                0,
                [], AvatarFrameId: from?.AvatarFrameId));
        }

        foreach (var gift in gifts)
        {
            items.Add(new InboxItem(
                InboxKinds.Gift,
                gift.Id,
                gift.CreatedAt,
                null,
                null,
                null,
                gift.Gold,
                gift.Cards,
                ResolveTitleKey(gift.TitleKey, gift.Gold, gift.Cards)));
        }

        return items
            .OrderByDescending(item => item.CreatedAt)
            .ThenByDescending(item => item.Id)
            .ToList();
    }

    public async Task<ClaimGiftResponse> ClaimGiftAsync(
        Guid userId,
        Guid giftId,
        ClaimGiftRequest request,
        CancellationToken cancellationToken)
    {
        var gift = await db.Gifts.SingleOrDefaultAsync(value => value.Id == giftId, cancellationToken);
        if (gift is null || gift.TargetUserId != userId)
        {
            throw new ApiException(StatusCodes.Status404NotFound, "GIFT_CLAIM_FAILED", "礼品不存在或无法领取");
        }

        if (gift.Claimed)
        {
            throw new ApiException(StatusCodes.Status409Conflict, "GIFT_CLAIM_FAILED", "该礼品已领取");
        }

        var profile = await EnsurePlayerAsync(userId, cancellationToken);
        if (profile.Revision != request.ExpectedRevision)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "PLAYER_DATA_CHANGED",
                "玩家数据已发生变化，请刷新后重试");
        }

        if (gift.Gold > 0 && profile.Gold > long.MaxValue - gift.Gold)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "GOLD_OVERFLOW", "金币数量超出上限");
        }

        var configuration = await configReader.GetAsync(cancellationToken);
        var settlement = CardInventorySettlement.Grant(profile.OwnedCards,
            gift.Cards.Select(card => card with { CardId = configuration.ResolveCardId(card.CardId) }),
            CardEconomyConfiguration.From(configuration));
        long ur = CardInventorySettlement.AddUr(profile.Ur, settlement.UrGained);
        var now = timeProvider.GetUtcNow();
        if (gift.Gold > 0)
        {
            profile.Gold += gift.Gold;
        }

        profile.OwnedCards = settlement.Cards;
        profile.OwnedArtIds = profile.OwnedArtIds.Concat(gift.Cards.Select(card => card.CardId))
            .Distinct(StringComparer.Ordinal).OrderBy(id => id, StringComparer.Ordinal).ToList();
        profile.Ur = ur;

        profile.Revision++;
        profile.UpdatedAt = now;
        gift.Claimed = true;
        gift.ClaimedAt = now;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ApiException(
                StatusCodes.Status409Conflict,
                "PLAYER_DATA_CHANGED",
                "玩家数据已发生变化，请刷新后重试");
        }

        logger.LogInformation(
            "Player {UserId} claimed gift {GiftId} gold={Gold} cards={CardCount} at revision {Revision}.",
            userId,
            giftId,
            gift.Gold,
            gift.Cards.Count,
            profile.Revision);
        return new ClaimGiftResponse(PlayerService.ToResponse(profile), settlement.UrGained);
    }

    public async Task<AdminGrantGiftResponse> GrantGiftByUsernameAsync(
        AdminGrantGiftRequest request,
        CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim() ?? string.Empty;
        if (username.Length == 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_USERNAME", "账号不能为空");
        }

        var cards = NormalizeCards(request.Cards);
        if (request.Gold < 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_GIFT", "金币不能为负数");
        }

        if (request.Gold == 0 && cards.Count == 0)
        {
            throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_GIFT", "礼品至少包含金币或卡牌");
        }

        var normalized = username.ToUpperInvariant();
        var account = await db.Users
            .Include(value => value.PlayerProfile)
            .SingleOrDefaultAsync(value => value.NormalizedUsername == normalized, cancellationToken);
        if (account is null)
        {
            throw new ApiException(StatusCodes.Status404NotFound, "ACCOUNT_NOT_FOUND", "未找到该账号");
        }

        var now = timeProvider.GetUtcNow();
        if (account.PlayerProfile is null)
        {
            db.PlayerProfiles.Add(PlayerProfile.ForNewAccount(account.Id, account.Username, now));
        }

        var gift = new Gift
        {
            Id = Guid.NewGuid(),
            TargetUserId = account.Id,
            SourceUserId = null,
            Gold = request.Gold,
            TitleKey = ResolveTitleKey(request.TitleKey, request.Gold, cards),
            Cards = cards,
            Claimed = false,
            CreatedAt = now
        };
        db.Gifts.Add(gift);
        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation(
            "Granted inbox gift {GiftId} to {AccountId} ({Username}): gold={Gold} cards={CardCount} title={TitleKey}.",
            gift.Id,
            account.Id,
            account.Username,
            gift.Gold,
            gift.Cards.Count,
            gift.TitleKey);
        return new AdminGrantGiftResponse(gift.Id, account.Id);
    }

    internal static string ResolveTitleKey(string? requested, long gold, IReadOnlyList<OwnedCard> cards)
    {
        string key = requested?.Trim() ?? string.Empty;
        if (key.Length > 0)
        {
            return key;
        }

        bool hasCard = cards.Count > 0;
        if (gold > 0 && hasCard)
        {
            return "ui.gifts.pack_mixed";
        }

        return hasCard ? "ui.gifts.pack_card" : "ui.gifts.pack_gold";
    }

    private async Task<PlayerProfile> EnsurePlayerAsync(Guid userId, CancellationToken cancellationToken) =>
        await players.GetOrCreateAsync(userId, timeProvider.GetUtcNow(), cancellationToken) ??
        throw new ApiException(
            StatusCodes.Status401Unauthorized,
            "INVALID_ACCESS_TOKEN",
            "登录状态已失效，请重新登录");

    private async Task<HashSet<Guid>> LoadFriendIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var rows = await db.Friendships
            .AsNoTracking()
            .Where(value => value.UserIdA == userId || value.UserIdB == userId)
            .ToListAsync(cancellationToken);
        var ids = new HashSet<Guid>();
        foreach (var row in rows)
        {
            ids.Add(row.UserIdA == userId ? row.UserIdB : row.UserIdA);
        }

        return ids;
    }

    private async Task<HashSet<Guid>> LoadOutgoingPendingIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var ids = await db.FriendRequests
            .AsNoTracking()
            .Where(value => value.FromUserId == userId && value.Status == FriendRequestStatus.Pending)
            .Select(value => value.ToUserId)
            .ToListAsync(cancellationToken);
        return ids.ToHashSet();
    }

    private async Task<bool> AreFriendsAsync(Guid left, Guid right, CancellationToken cancellationToken)
    {
        var (a, b) = NormalizePair(left, right);
        return await db.Friendships.AnyAsync(
            value => value.UserIdA == a && value.UserIdB == b,
            cancellationToken);
    }

    private async Task<bool> HasPendingRequestAsync(Guid left, Guid right, CancellationToken cancellationToken) =>
        await db.FriendRequests.AnyAsync(
            value =>
                value.Status == FriendRequestStatus.Pending &&
                ((value.FromUserId == left && value.ToUserId == right) ||
                 (value.FromUserId == right && value.ToUserId == left)),
            cancellationToken);

    private async Task EnsureFriendshipAsync(
        Guid left,
        Guid right,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var (a, b) = NormalizePair(left, right);
        if (db.Friendships.Local.Any(value => value.UserIdA == a && value.UserIdB == b))
        {
            return;
        }

        if (await db.Friendships.AnyAsync(value => value.UserIdA == a && value.UserIdB == b, cancellationToken))
        {
            return;
        }

        db.Friendships.Add(new Friendship
        {
            UserIdA = a,
            UserIdB = b,
            CreatedAt = now
        });
    }

    private static (Guid A, Guid B) NormalizePair(Guid left, Guid right) =>
        left.CompareTo(right) < 0 ? (left, right) : (right, left);

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static List<OwnedCard> NormalizeCards(IReadOnlyList<AdminGiftCard>? cards)
    {
        if (cards is null || cards.Count == 0)
        {
            return [];
        }

        var merged = new Dictionary<(string CardId, int Rarity), int>();
        foreach (var card in cards)
        {
            var cardId = card.CardId?.Trim() ?? string.Empty;
            if (cardId.Length == 0 || card.Count <= 0)
            {
                throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_GIFT", "卡牌编号与数量必须有效");
            }

            if (card.Rarity < 0)
            {
                throw new ApiException(StatusCodes.Status400BadRequest, "INVALID_GIFT", "卡牌稀有度不能为负数");
            }

            var key = (cardId, card.Rarity);
            merged[key] = merged.TryGetValue(key, out var count) ? count + card.Count : card.Count;
        }

        return merged
            .Select(pair => new OwnedCard(pair.Key.CardId, pair.Key.Rarity, pair.Value))
            .OrderBy(card => card.CardId, StringComparer.Ordinal)
            .ThenBy(card => card.Rarity)
            .ToList();
    }


}

public static class InboxKinds
{
    public const string FriendRequest = "friendRequest";
    public const string Gift = "gift";
}
