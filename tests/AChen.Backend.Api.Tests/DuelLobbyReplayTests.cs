using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AChen.Backend.Api.Features.Duels;
using AChen.Duel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace AChen.Backend.Api.Tests;

public sealed class DuelLobbyReplayTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task LobbyPreviewAndPersistedReplayExposeOnlyAuthenticatedSeatData()
    {
        using var host = await Register();
        using var guest = await Register();
        using var outsider = await Register();
        var rooms = factory.Services.GetRequiredService<DuelRoomService>();
        var created = await host.PostAsync("/api/duel/rooms", null);
        created.EnsureSuccessStatusCode();
        var room = (await created.Content.ReadFromJsonAsync<DuelRoomView>())!;
        Assert.NotEmpty(room.Players[0].Nickname);
        Assert.True(room.Players[0].AvatarId > 0);
        Assert.Contains((await host.GetFromJsonAsync<DuelRoomSummary[]>("/api/duel/rooms"))!, x => x.Id == room.Id);
        Assert.Equal(room.Id, (await host.GetFromJsonAsync<DuelRoomView>("/api/duel/rooms/current"))!.Id);
        (await guest.PostAsJsonAsync("/api/duel/rooms/join", new { room.Code })).EnsureSuccessStatusCode();
        room = (await host.GetFromJsonAsync<DuelRoomView>($"/api/duel/rooms/{room.Id}"))!;
        Assert.DoesNotContain((await host.GetFromJsonAsync<DuelRoomSummary[]>("/api/duel/rooms"))!, x => x.Id == room.Id);
        var users = room.Players.Select(x => x.UserId).ToArray();
        var catalog = DuelCardCatalog.CreateDefault();
        var rules = CardRuleCatalog.CreateDefault(catalog);
        var deck = new DuelDeckRequest(catalog.Cards.Where(c => !c.IsExtra && rules.Get(c.CardId).Support.IsComplete)
            .SelectMany(c => Enumerable.Repeat(c.CardId, c.MaxCopies)).Take(40).ToArray(), Array.Empty<string>());
        foreach (var user in users)
        {
            rooms.SubmitDeck(user, room.Id, deck);
            rooms.Connect(user, room.Id);
            rooms.SetReady(user, room.Id, true);
        }
        var own = await host.GetFromJsonAsync<DuelZonePreview>($"/api/duel/rooms/{room.Id}/preview?seat=0&zone=Deck");
        var hidden = await host.GetFromJsonAsync<DuelZonePreview>($"/api/duel/rooms/{room.Id}/preview?seat=1&zone=Deck");
        Assert.Equal(35, own!.Cards.Count);
        Assert.Equal(own.Cards.Select(c => c.DefinitionId).OrderBy(x => x, StringComparer.Ordinal), own.Cards.Select(c => c.DefinitionId));
        Assert.All(own.Cards, card => { Assert.True(card.Known); Assert.Empty(card.ViewCardId); });
        Assert.All(hidden!.Cards, card => { Assert.False(card.Known); Assert.Empty(card.DefinitionId); Assert.Empty(card.ViewCardId); });
        Assert.Equal(HttpStatusCode.Forbidden, (await outsider.GetAsync($"/api/duel/rooms/{room.Id}/preview?seat=0&zone=Deck")).StatusCode);
        var duel = rooms.Get(users[0], room.Id).Duel!;
        var end = rooms.Submit(users[0], room.Id, new DuelInputRequest(Guid.NewGuid(), new SeatInput
        { Revision = duel.Revision, ActionToken = duel.Actions.Single(x => x.Kind == DuelCommandKind.Surrender).ActionToken }));
        Assert.NotEmpty(end.Frames);
        Assert.All(end.Frames, frame => { Assert.Empty(frame.Snapshot.Actions); Assert.Null(frame.Snapshot.Decision); });
        var replacement = rooms.Connect(users[0], room.Id);
        Assert.Empty((await replacement.Updates.ReadAsync()).Frames);
        var summaries = (await host.GetFromJsonAsync<DuelReplaySummary[]>("/api/duel/replays"))!;
        var replay = Assert.Single(summaries.Where(x => x.Players.Any(p => p.UserId == users[1])));
        Assert.Equal(1, replay.Winner);
        using var detail = await host.GetFromJsonAsync<JsonDocument>($"/api/duel/replays/{replay.Id}");
        Assert.Equal(2, detail!.RootElement.GetProperty("tracks").GetArrayLength());
        Assert.DoesNotContain("randomState", detail.RootElement.GetRawText());
        Assert.DoesNotContain("seed", detail.RootElement.GetRawText());
        Assert.Equal(HttpStatusCode.NotFound, (await outsider.GetAsync($"/api/duel/replays/{replay.Id}")).StatusCode);
        rooms.Leave(users[0], room.Id);
        var reopenedStore = new DuelReplayStore(factory.Services.GetRequiredService<IServiceScopeFactory>());
        Assert.Contains(reopenedStore.List(users[0]), x => x.Id == replay.Id);
    }
    async Task<HttpClient> Register()
    {
        var client = factory.CreateClient();
        var result = await client.PostAsJsonAsync("/api/auth/register", new
        { username = "Lobby" + Guid.NewGuid().ToString("N")[..12], password = "correct-horse-42" });
        result.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await result.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }
}
