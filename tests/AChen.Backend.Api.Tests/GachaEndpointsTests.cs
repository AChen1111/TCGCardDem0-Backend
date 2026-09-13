using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AChen.Backend.Api.Features.Gacha;
using AChen.Backend.Api.Features.Players;

namespace AChen.Backend.Api.Tests;

public sealed class GachaEndpointsTests
{
    [Fact]
    public async Task Draw_requires_imported_config()
    {
        using var factory = new ApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory, "NoGacha");
        var player = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        var response = await client.PostAsJsonAsync("/api/player/card-draws", new
        {
            poolKey = "Card01",
            count = 1,
            expectedRevision = player!.Revision
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorCodeAsync(response, "GACHA_CONFIG_EMPTY");
    }

    [Fact]
    public async Task Draw_rejects_unknown_pool_and_invalid_count()
    {
        using var factory = new ApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory, "BadDraw");
        await ImportAsync(factory, SingleCardCsv("Card01", "26077389", rarity0: 1, rarity1: 0));
        var player = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");

        var missing = await client.PostAsJsonAsync("/api/player/card-draws", new
        {
            poolKey = "Card99",
            count = 1,
            expectedRevision = player!.Revision
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, missing.StatusCode);
        await AssertErrorCodeAsync(missing, "GACHA_POOL_NOT_FOUND");

        var invalidCount = await client.PostAsJsonAsync("/api/player/card-draws", new
        {
            poolKey = "Card01",
            count = 0,
            expectedRevision = player.Revision
        });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, invalidCount.StatusCode);
        await AssertErrorCodeAsync(invalidCount, "INVALID_DRAW_COUNT");
    }

    [Fact]
    public async Task Draw_returns_string_card_id_and_stacks_same_rarity()
    {
        var random = new ScriptedGachaRandom(0, 0, 0, 0);
        using var factory = new ApiFactory { GachaRandom = random };
        using var client = await CreateAuthenticatedClientAsync(factory, "StackDraw");
        await ImportAsync(factory, SingleCardCsv("Card01", "26077389", rarity0: 1, rarity1: 0));
        var player = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");
        Assert.Empty(player!.OwnedCards);

        var response = await client.PostAsJsonAsync("/api/player/card-draws", new
        {
            poolKey = "Card01",
            count = 2,
            expectedRevision = player.Revision
        });
        response.EnsureSuccessStatusCode();
        var drawn = await response.Content.ReadFromJsonAsync<DrawPayload>();

        Assert.NotNull(drawn);
        Assert.Equal(2, drawn.Results.Count);
        Assert.All(drawn.Results, result =>
        {
            Assert.Equal("26077389", result.CardId);
            Assert.InRange(result.Rarity, 0, 3);
        });
        var owned = Assert.Single(drawn.Player.OwnedCards);
        Assert.Equal("26077389", owned.CardId);
        Assert.Equal(0, owned.Rarity);
        Assert.Equal(2, owned.Count);
        Assert.Equal(player.Revision + 1, drawn.Player.Revision);
        Assert.Equal(player.Gold, drawn.Player.Gold);
        Assert.All(drawn.Results, result => Assert.Equal("Card01", result.SourcePool));
    }

    [Fact]
    public async Task Draw_keeps_same_card_different_rarity_as_two_rows()
    {
        var random = new ScriptedGachaRandom(0, 0, 0, 1);
        using var factory = new ApiFactory { GachaRandom = random };
        using var client = await CreateAuthenticatedClientAsync(factory, "SplitRarity");
        await ImportAsync(factory, SingleCardCsv("Card01", "26077389", rarity0: 1, rarity1: 1));
        var player = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");

        var response = await client.PostAsJsonAsync("/api/player/card-draws", new
        {
            poolKey = "Card01",
            count = 2,
            expectedRevision = player!.Revision
        });
        response.EnsureSuccessStatusCode();
        var drawn = await response.Content.ReadFromJsonAsync<DrawPayload>();

        Assert.NotNull(drawn);
        Assert.Equal(2, drawn.Player.OwnedCards.Count);
        Assert.Contains(drawn.Player.OwnedCards, card => card.CardId == "26077389" && card.Rarity == 0 && card.Count == 1);
        Assert.Contains(drawn.Player.OwnedCards, card => card.CardId == "26077389" && card.Rarity == 1 && card.Count == 1);
    }

    [Fact]
    public async Task Draw_card_all_uses_catalog_and_returns_source_pool()
    {
        var random = new ScriptedGachaRandom(0, 0);
        using var factory = new ApiFactory { GachaRandom = random };
        using var client = await CreateAuthenticatedClientAsync(factory, "AllCardDraw");
        await ImportAsync(factory, SingleCardCsv("Card01", "14558127", rarity0: 1, rarity1: 0));
        await ImportAllCardsAsync(factory, "CardId,SourcePool\r\n26077389,Card03\r\n");
        var player = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");

        var response = await client.PostAsJsonAsync("/api/player/card-draws", new
        {
            poolKey = "CardAll",
            count = 1,
            expectedRevision = player!.Revision
        });
        response.EnsureSuccessStatusCode();
        var drawn = await response.Content.ReadFromJsonAsync<DrawPayload>();

        Assert.NotNull(drawn);
        var result = Assert.Single(drawn.Results);
        Assert.Equal("26077389", result.CardId);
        Assert.Equal("Card03", result.SourcePool);
        Assert.Equal(0, result.Rarity);
    }

    [Fact]
    public async Task Draw_card_all_requires_catalog()
    {
        using var factory = new ApiFactory();
        using var client = await CreateAuthenticatedClientAsync(factory, "NoAllCards");
        await ImportAsync(factory, SingleCardCsv("Card01", "14558127", rarity0: 1, rarity1: 0));
        var player = await client.GetFromJsonAsync<PlayerPayload>("/api/player/bootstrap");

        var response = await client.PostAsJsonAsync("/api/player/card-draws", new
        {
            poolKey = "CardAll",
            count = 1,
            expectedRevision = player!.Revision
        });

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        await AssertErrorCodeAsync(response, "ALL_CARDS_EMPTY");
    }

    private static async Task ImportAsync(ApiFactory factory, string csv)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Content-Publish-Key", ApiFactory.PublishKey);
        using var content = new StringContent(csv, Encoding.UTF8, "text/csv");
        var response = await client.PutAsync("/api/gacha/admin/config", content);
        response.EnsureSuccessStatusCode();
        var imported = await response.Content.ReadFromJsonAsync<GachaConfigResponse>();
        Assert.NotNull(imported);
        Assert.True(imported.PoolEntryCount > 0);
        Assert.True(imported.RarityWeightCount > 0);
    }

    private static async Task ImportAllCardsAsync(ApiFactory factory, string csv)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Content-Publish-Key", ApiFactory.PublishKey);
        using var content = new StringContent(csv, Encoding.UTF8, "text/csv");
        var response = await client.PutAsync("/api/gacha/admin/cards", content);
        response.EnsureSuccessStatusCode();
        var imported = await response.Content.ReadFromJsonAsync<AllCardsConfigResponse>();
        Assert.NotNull(imported);
        Assert.True(imported.CardCount > 0);
    }

    private static string SingleCardCsv(string poolKey, string cardId, int rarity0, int rarity1)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Table,PoolKey,CardId,Weight,Rarity");
        builder.AppendLine($"Card,{poolKey},{cardId},100,");
        if (rarity0 > 0)
        {
            builder.AppendLine($"Rarity,,,{rarity0},0");
        }

        if (rarity1 > 0)
        {
            builder.AppendLine($"Rarity,,,{rarity1},1");
        }

        return builder.ToString();
    }

    private static async Task<HttpClient> CreateAuthenticatedClientAsync(ApiFactory factory, string username)
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
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private static async Task AssertErrorCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
    }

    private sealed class ScriptedGachaRandom(params int[] rolls) : IGachaRandom
    {
        private readonly Queue<int> values = new(rolls);

        public int Next(int exclusiveUpperBound) => values.Count == 0 ? 0 : values.Dequeue();
    }

    private sealed record AuthPayload(string AccessToken);

    private sealed record OwnedCardPayload(string CardId, int Rarity, int Count);

    private sealed record PlayerPayload(
        Guid Id,
        string Nickname,
        IReadOnlyList<OwnedCardPayload> OwnedCards,
        long Gold,
        long Revision);

    private sealed record DrawResultPayload(string CardId, int Rarity, string SourcePool);

    private sealed record DrawPayload(IReadOnlyList<DrawResultPayload> Results, PlayerPayload Player);
}
