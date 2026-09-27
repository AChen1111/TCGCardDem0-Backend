using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace AChen.Backend.Api.Tests;

public sealed class DeckEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly PublishedConfigFixture config = new(factory);
    private const string Root = "/api/player/decks";

    [Fact]
    public async Task Created_empty_draft_can_be_retrieved_in_a_new_session()
    {
        string username = "Deck" + Guid.NewGuid().ToString("N")[..12];
        using var client = await SignInAsync(username, true);
        var response = await client.PostAsJsonAsync(Root, new { name = "  青眼  " });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = (await response.Content.ReadFromJsonAsync<DeckPayload>())!;
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("青眼", created.Name);
        Assert.Empty(created.MainDeck);
        Assert.Empty(created.ExtraDeck);
        Assert.Equal(0, created.Revision);

        using var restored = await SignInAsync(username, false);
        var loaded = await restored.GetFromJsonAsync<DeckPayload>($"{Root}/{created.Id}");
        Assert.NotNull(loaded);
        Assert.Equal(created.Id, loaded.Id);
        Assert.Equal(created.Name, loaded.Name);
        Assert.Equal(created.Revision, loaded.Revision);
        Assert.Empty(loaded.MainDeck);
        Assert.Empty(loaded.ExtraDeck);
        Assert.InRange((created.CreatedAt - loaded.CreatedAt).Duration(), TimeSpan.Zero, TimeSpan.FromMilliseconds(1));
        var list = await restored.GetFromJsonAsync<DeckPayload[]>(Root);
        Assert.Equal(created.Id, Assert.Single(list!).Id);
    }

    [Fact]
    public async Task Save_replaces_draft_and_rejects_stale_writes_without_enforcing_card_rules()
    {
        using var client = await SignInAsync("Save" + Guid.NewGuid().ToString("N")[..12], true);
        var created = (await (await client.PostAsJsonAsync(Root, new { name = "初稿" })).Content.ReadFromJsonAsync<DeckPayload>())!;
        var playerBefore = await client.GetStringAsync("/api/player/bootstrap");
        // Unknown, unowned, wrong-section and excessive copies are intentionally accepted by storage.
        var body = new { name = "修改后", mainDeck = new[] { new Entry("01639384", 0, 61) },
            extraDeck = new[] { new Entry("unknown-card", 1, 16) }, expectedRevision = 0 };
        var save = await client.PutAsJsonAsync($"{Root}/{created.Id}", body);
        save.EnsureSuccessStatusCode();
        var saved = (await save.Content.ReadFromJsonAsync<DeckPayload>())!;
        Assert.Equal(1, saved.Revision);
        Assert.Equal("修改后", saved.Name);
        Assert.Equal(body.mainDeck, saved.MainDeck);
        Assert.Equal(body.extraDeck, saved.ExtraDeck);
        var stale = await client.PutAsJsonAsync($"{Root}/{created.Id}", body);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("DECK_DATA_CHANGED", JsonDocument.Parse(await stale.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
        var loaded = (await client.GetFromJsonAsync<DeckPayload>($"{Root}/{created.Id}"))!;
        Assert.Equal(saved.Revision, loaded.Revision);
        Assert.Equal(saved.MainDeck, loaded.MainDeck);
        Assert.Equal(playerBefore, await client.GetStringAsync("/api/player/bootstrap"));
    }

    [Fact]
    public async Task Delete_requires_current_revision_and_removes_only_the_owned_deck()
    {
        using var owner = await SignInAsync("Owner" + Guid.NewGuid().ToString("N")[..12], true);
        using var other = await SignInAsync("Other" + Guid.NewGuid().ToString("N")[..12], true);
        var created = (await (await owner.PostAsJsonAsync(Root, new { name = "draft" })).Content.ReadFromJsonAsync<DeckPayload>())!;
        Assert.Empty((await other.GetFromJsonAsync<DeckPayload[]>(Root))!);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"{Root}/{created.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"{Root}/{created.Id}?expectedRevision=0")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await owner.DeleteAsync($"{Root}/{created.Id}?expectedRevision=1")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"{Root}/{created.Id}?expectedRevision=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync($"{Root}/{created.Id}")).StatusCode);
        Assert.Empty((await owner.GetFromJsonAsync<DeckPayload[]>(Root))!);
    }

    [Theory]
    [InlineData("{\"name\":\" \"}")]
    [InlineData("{\"name\":null}")]
    [InlineData("{\"name\":\"a\\nb\"}")]
    public async Task Invalid_name_is_rejected_without_creating_a_deck(string json)
    {
        using var client = await SignInAsync("Invalid" + Guid.NewGuid().ToString("N")[..12], true);
        var response = await client.PostAsync(Root, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<DeckPayload[]>(Root))!);
    }

    [Theory]
    [InlineData("{\"name\":\"draft\",\"mainDeck\":null,\"extraDeck\":[],\"expectedRevision\":0}")]
    [InlineData("{\"name\":\"draft\",\"mainDeck\":[null],\"extraDeck\":[],\"expectedRevision\":0}")]
    [InlineData("{\"name\":\"draft\",\"mainDeck\":[{\"cardId\":\"\",\"rarity\":0,\"count\":1}],\"extraDeck\":[],\"expectedRevision\":0}")]
    [InlineData("{\"name\":\"draft\",\"mainDeck\":[{\"cardId\":\"001\",\"rarity\":0,\"count\":0}],\"extraDeck\":[],\"expectedRevision\":0}")]
    [InlineData("{\"name\":\"draft\",\"mainDeck\":[],\"extraDeck\":[]}")]
    public async Task Invalid_save_structure_leaves_saved_version_intact(string json)
    {
        using var client = await SignInAsync("Shape" + Guid.NewGuid().ToString("N")[..12], true);
        var created = (await (await client.PostAsJsonAsync(Root, new { name = "before" })).Content.ReadFromJsonAsync<DeckPayload>())!;
        var response = await client.PutAsync($"{Root}/{created.Id}", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var loaded = (await client.GetFromJsonAsync<DeckPayload>($"{Root}/{created.Id}"))!;
        Assert.Equal("before", loaded.Name);
        Assert.Equal(0, loaded.Revision);
    }

    [Fact]
    public async Task All_deck_routes_require_authentication()
    {
        using var client = factory.CreateClient();
        string path = $"{Root}/{Guid.NewGuid()}";
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Root)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync(Root, new { name = "draft" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(path, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.DeleteAsync(path + "?expectedRevision=0")).StatusCode);
    }

    [Fact]
    public async Task Concurrent_saves_cannot_both_replace_the_same_revision()
    {
        using var client = await SignInAsync("Concurrent" + Guid.NewGuid().ToString("N")[..10], true);
        var created = (await (await client.PostAsJsonAsync(Root, new { name = "before" })).Content.ReadFromJsonAsync<DeckPayload>())!;
        var body = new { name = "after", mainDeck = Array.Empty<Entry>(), extraDeck = Array.Empty<Entry>(), expectedRevision = 0 };
        var responses = await Task.WhenAll(client.PutAsJsonAsync($"{Root}/{created.Id}", body), client.PutAsJsonAsync($"{Root}/{created.Id}", body));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal(1, (await client.GetFromJsonAsync<DeckPayload>($"{Root}/{created.Id}"))!.Revision);
    }

    private async Task<HttpClient> SignInAsync(string username, bool register)
    {
        if (config.ReleaseId is null) await config.PublishAsync();
        var client = factory.CreateClient();
        config.Attach(client);
        var response = await client.PostAsJsonAsync(register ? "/api/auth/register" : "/api/auth/login",
            new { username, password = "correct-horse-42" });
        response.EnsureSuccessStatusCode();
        using var auth = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            auth.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    private sealed record Entry(string CardId, int Rarity, int Count);
    private sealed record DeckPayload(Guid Id, string Name, Entry[] MainDeck, Entry[] ExtraDeck,
        long Revision, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
}
