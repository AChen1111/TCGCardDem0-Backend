using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AChen.Backend.Api.Features.Players;
using AChen.Backend.Api.Features.Social;

namespace AChen.Backend.Api.Tests;

public sealed class SocialEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Search_finds_other_players_by_nickname_contains()
    {
        var (alice, _) = await CreateAuthenticatedClientAsync("SocSearchA");
        var (bob, bobPlayer) = await CreateAuthenticatedClientAsync("SocSearchB");

        var response = await alice.GetAsync("/api/friends/search?nickname=SearchB");
        response.EnsureSuccessStatusCode();
        var hits = await response.Content.ReadFromJsonAsync<List<FriendSearchHit>>();

        Assert.NotNull(hits);
        Assert.Contains(hits, hit => hit.Id == bobPlayer.Id && hit.Nickname == "SocSearchB" && !hit.IsFriend);
        Assert.DoesNotContain(hits, hit => hit.Nickname == "SocSearchA");
    }

    [Fact]
    public async Task Cannot_add_self_as_friend()
    {
        var (client, me) = await CreateAuthenticatedClientAsync("SocSelf");

        var response = await client.PostAsJsonAsync("/api/friends/requests", new { targetPlayerId = me.Id });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await AssertErrorCodeAsync(response, "FRIEND_SELF");
    }

    [Fact]
    public async Task Duplicate_pending_request_is_rejected()
    {
        var (alice, _) = await CreateAuthenticatedClientAsync("SocDupA");
        var (bob, bobPlayer) = await CreateAuthenticatedClientAsync("SocDupB");

        var first = await alice.PostAsJsonAsync("/api/friends/requests", new { targetPlayerId = bobPlayer.Id });
        first.EnsureSuccessStatusCode();
        var search = await alice.GetFromJsonAsync<List<FriendSearchHit>>("/api/friends/search?nickname=SocDupB");
        Assert.Contains(search, hit => hit.Id == bobPlayer.Id && hit.IsPending && !hit.IsFriend);
        var second = await alice.PostAsJsonAsync("/api/friends/requests", new { targetPlayerId = bobPlayer.Id });

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        await AssertErrorCodeAsync(second, "FRIEND_PENDING");
    }

    [Fact]
    public async Task Accept_request_adds_both_sides_to_friend_list()
    {
        var (alice, alicePlayer) = await CreateAuthenticatedClientAsync("SocAccA");
        var (bob, bobPlayer) = await CreateAuthenticatedClientAsync("SocAccB");

        var created = await alice.PostAsJsonAsync("/api/friends/requests", new { targetPlayerId = bobPlayer.Id });
        created.EnsureSuccessStatusCode();
        var request = await created.Content.ReadFromJsonAsync<FriendRequestCreated>();
        Assert.NotNull(request);

        var inbox = await bob.GetFromJsonAsync<List<InboxItem>>("/api/inbox");
        Assert.NotNull(inbox);
        Assert.Contains(inbox, item =>
            item.Kind == InboxKinds.FriendRequest &&
            item.Id == request.Id &&
            item.PlayerId == alicePlayer.Id);

        var accept = await bob.PostAsync($"/api/friends/requests/{request.Id}/accept", null);
        accept.EnsureSuccessStatusCode();

        var aliceFriends = await alice.GetFromJsonAsync<List<FriendSummary>>("/api/friends");
        var bobFriends = await bob.GetFromJsonAsync<List<FriendSummary>>("/api/friends");
        Assert.NotNull(aliceFriends);
        Assert.NotNull(bobFriends);
        Assert.Contains(aliceFriends, friend => friend.Id == bobPlayer.Id && friend.Nickname == "SocAccB");
        Assert.Contains(bobFriends, friend => friend.Id == alicePlayer.Id && friend.Nickname == "SocAccA");

        var search = await alice.GetFromJsonAsync<List<FriendSearchHit>>("/api/friends/search?nickname=SocAccB");
        Assert.NotNull(search);
        Assert.Contains(search, hit => hit.Id == bobPlayer.Id && hit.IsFriend);

        var afterInbox = await bob.GetFromJsonAsync<List<InboxItem>>("/api/inbox");
        Assert.NotNull(afterInbox);
        Assert.DoesNotContain(afterInbox, item => item.Id == request.Id);
    }

    [Fact]
    public async Task Reject_request_does_not_create_friendship()
    {
        var (alice, _) = await CreateAuthenticatedClientAsync("SocRejA");
        var (bob, bobPlayer) = await CreateAuthenticatedClientAsync("SocRejB");

        var created = await alice.PostAsJsonAsync("/api/friends/requests", new { targetPlayerId = bobPlayer.Id });
        created.EnsureSuccessStatusCode();
        var request = await created.Content.ReadFromJsonAsync<FriendRequestCreated>();
        Assert.NotNull(request);

        var reject = await bob.PostAsync($"/api/friends/requests/{request.Id}/reject", null);
        reject.EnsureSuccessStatusCode();

        var aliceFriends = await alice.GetFromJsonAsync<List<FriendSummary>>("/api/friends");
        var bobFriends = await bob.GetFromJsonAsync<List<FriendSummary>>("/api/friends");
        Assert.NotNull(aliceFriends);
        Assert.NotNull(bobFriends);
        Assert.Empty(aliceFriends);
        Assert.Empty(bobFriends);

        var inbox = await bob.GetFromJsonAsync<List<InboxItem>>("/api/inbox");
        Assert.NotNull(inbox);
        Assert.DoesNotContain(inbox, item => item.Id == request.Id);
    }

    [Fact]
    public async Task Admin_gift_can_be_claimed_once_and_grants_gold_and_cards()
    {
        var (playerClient, before) = await CreateAuthenticatedClientAsync("SocGiftP");

        using var publisher = factory.CreateClient();
        publisher.DefaultRequestHeaders.Add("X-Content-Publish-Key", ApiFactory.PublishKey);
        var grant = await publisher.PostAsJsonAsync("/api/accounts/admin/gifts", new
        {
            username = "SocGiftP",
            gold = 250,
            cards = new[]
            {
                new { cardId = "26077389", count = 2, rarity = 1 }
            }
        });
        grant.EnsureSuccessStatusCode();
        var granted = await grant.Content.ReadFromJsonAsync<AdminGrantGiftResponse>();
        Assert.NotNull(granted);
        Assert.Equal(before.Id, granted.TargetPlayerId);

        var inbox = await playerClient.GetFromJsonAsync<List<InboxItem>>("/api/inbox");
        Assert.NotNull(inbox);
        var gift = Assert.Single(inbox, item => item.Kind == InboxKinds.Gift && item.Id == granted.GiftId);
        Assert.Null(gift.PlayerId);
        Assert.Equal(250, gift.Gold);
        Assert.Equal("26077389", gift.Cards[0].CardId);
        Assert.Equal(1, gift.Cards[0].Rarity);
        Assert.Equal(2, gift.Cards[0].Count);

        var claim = await playerClient.PostAsJsonAsync($"/api/gifts/{granted.GiftId}/claim", new
        {
            expectedRevision = before.Revision
        });
        claim.EnsureSuccessStatusCode();
        var after = await claim.Content.ReadFromJsonAsync<PlayerPayload>();
        Assert.NotNull(after);
        Assert.Equal(before.Gold + 250, after.Gold);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Contains(after.OwnedCards, card => card.CardId == "26077389" && card.Rarity == 1 && card.Count == 2);

        var emptyInbox = await playerClient.GetFromJsonAsync<List<InboxItem>>("/api/inbox");
        Assert.NotNull(emptyInbox);
        Assert.DoesNotContain(emptyInbox, item => item.Id == granted.GiftId);

        var again = await playerClient.PostAsJsonAsync($"/api/gifts/{granted.GiftId}/claim", new
        {
            expectedRevision = after.Revision
        });
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        await AssertErrorCodeAsync(again, "GIFT_CLAIM_FAILED");
    }

    [Fact]
    public async Task Missing_player_request_returns_not_found()
    {
        var (client, _) = await CreateAuthenticatedClientAsync("SocMiss");
        var response = await client.PostAsJsonAsync(
            "/api/friends/requests",
            new { targetPlayerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa") });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertErrorCodeAsync(response, "FRIEND_NOT_FOUND");
    }

    private async Task<(HttpClient Client, PlayerPayload Player)> CreateAuthenticatedClientAsync(string username)
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        {
            username,
            password = "correct-horse-42"
        });
        response.EnsureSuccessStatusCode();
        var auth = await response.Content.ReadFromJsonAsync<AuthPayload>();
        Assert.NotNull(auth);
        Assert.NotNull(auth.Player);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth.Player);
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
        Assert.True(response.Headers.Contains("X-Request-Id"));
    }

    private sealed record AuthPayload(string AccessToken, PlayerPayload Player);

    private sealed record PlayerPayload(
        Guid Id,
        string Nickname,
        int? AvatarId,
        IReadOnlyList<int> OwnedAvatarIds,
        int? BackgroundId,
        IReadOnlyList<int> OwnedBackgroundIds,
        IReadOnlyList<OwnedCard> OwnedCards,
        long Gold,
        long Revision);
}
