using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using AChen.Backend.Api.Features.Duels;
using AChen.Duel.Core;
using Microsoft.Extensions.DependencyInjection;

namespace AChen.Backend.Api.Tests;

// 使用生产规则包与支持门，不注入传输测试专用的全支持声明。
public sealed class DuelProductionFlowTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task TwoSignedAccountsCompleteAndReplayADuelThenPrepareAnotherMatchInTheSameRoom()
    {
        using var host = await Register();
        using var guest = await Register();
        var created = await host.PostAsync("/api/duel/rooms", null);
        created.EnsureSuccessStatusCode();
        var room = (await created.Content.ReadFromJsonAsync<DuelRoomView>())!;
        (await guest.PostAsJsonAsync("/api/duel/rooms/join", new { room.Code })).EnsureSuccessStatusCode();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var hostSocket = await Connect(host, room.Id, timeout.Token);
        using var guestSocket = await Connect(guest, room.Id, timeout.Token);
        var printed = DuelCardCatalog.CreateDefault();
        var rules = CardRuleCatalog.CreateDefault(printed);
        var deck = new DuelDeckRequest(printed.Cards.Where(c => !c.IsExtra && rules.Get(c.CardId).Support.IsComplete)
            .SelectMany(c => Enumerable.Repeat(c.CardId, c.MaxCopies)).Take(40).ToArray(), Array.Empty<string>());
        Assert.Equal(40, deck.MainDeck.Length);
        Assert.Empty(rules.CheckDeckSupport(deck.MainDeck));
        foreach (var client in new[] { host, guest })
        {
            (await client.PutAsJsonAsync($"/api/duel/rooms/{room.Id}/deck", deck)).EnsureSuccessStatusCode();
            (await client.PostAsJsonAsync($"/api/duel/rooms/{room.Id}/ready", new { ready = true })).EnsureSuccessStatusCode();
        }
        using var initial = await State(host, room.Id);
        Assert.Equal("Running", initial.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain("randomState", initial.RootElement.GetRawText());
        Assert.DoesNotContain("seed", initial.RootElement.GetRawText());
        Assert.DoesNotContain("replay", initial.RootElement.GetRawText());
        int waiting = initial.RootElement.GetProperty("duel").GetProperty("waitingSeat").GetInt32();
        var currentClient = waiting == 0 ? host : guest;
        var currentSocket = waiting == 0 ? hostSocket : guestSocket;
        using var current = await State(currentClient, room.Id);
        var pass = Input(current.RootElement.GetProperty("duel"), DuelCommandKind.Pass);
        Guid passRequest = Guid.NewGuid();
        await Send(currentSocket, passRequest, pass, timeout.Token);
        using var passed = await Receipt(currentSocket, passRequest, timeout.Token);
        Assert.True(passed.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean());
        using var beforeEnd = await State(host, room.Id);
        var surrender = Input(beforeEnd.RootElement.GetProperty("duel"), DuelCommandKind.Surrender);
        Guid endRequest = Guid.NewGuid();
        await Send(hostSocket, endRequest, surrender, timeout.Token);
        using var ended = await Receipt(hostSocket, endRequest, timeout.Token);
        Assert.Equal("Finished", ended.RootElement.GetProperty("room").GetProperty("status").GetString());
        Assert.Equal(1, ended.RootElement.GetProperty("room").GetProperty("duel").GetProperty("winner").GetInt32());
        await Send(hostSocket, endRequest, surrender, timeout.Token);
        using var duplicate = await Receipt(hostSocket, endRequest, timeout.Token);
        Assert.Equal(ended.RootElement.GetProperty("result").GetProperty("revision").GetInt64(),
            duplicate.RootElement.GetProperty("result").GetProperty("revision").GetInt64());
        var privateReplay = factory.Services.GetRequiredService<DuelRoomService>().CaptureReplay(room.Id);
        Assert.Equal(new[] { DuelCommandKind.Pass, DuelCommandKind.Surrender }, privateReplay.Entries.Select(e => e.Command.Kind));
        Assert.True(DuelReplayRunner.Run(printed, privateReplay).Verified);
        var returned = await host.PostAsync($"/api/duel/rooms/{room.Id}/return", null);
        returned.EnsureSuccessStatusCode();
        using var lobby = JsonDocument.Parse(await returned.Content.ReadAsStringAsync());
        Assert.Equal("Waiting", lobby.RootElement.GetProperty("status").GetString());
        Assert.All(lobby.RootElement.GetProperty("players").EnumerateArray(), p => Assert.False(p.GetProperty("ready").GetBoolean()));
        foreach (var client in new[] { host, guest })
            (await client.PostAsJsonAsync($"/api/duel/rooms/{room.Id}/ready", new { ready = true })).EnsureSuccessStatusCode();
        using var next = await State(host, room.Id);
        Assert.Equal("Running", next.RootElement.GetProperty("status").GetString());
        Assert.Equal(0, next.RootElement.GetProperty("duel").GetProperty("revision").GetInt64());
        Assert.Equal(initial.RootElement.GetProperty("rulePackageHash").GetString(), next.RootElement.GetProperty("rulePackageHash").GetString());
    }

    async Task<HttpClient> Register()
    {
        var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new {
            username = "ProdDuel" + Guid.NewGuid().ToString("N")[..10], password = "correct-horse-42" });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    async Task<WebSocket> Connect(HttpClient client, Guid roomId, CancellationToken token)
    {
        var socket = factory.Server.CreateWebSocketClient();
        socket.ConfigureRequest = request => request.Headers["Authorization"] = client.DefaultRequestHeaders.Authorization!.ToString();
        return await socket.ConnectAsync(new Uri($"ws://localhost/api/duel/rooms/{roomId}/socket?protocolVersion=1"), token);
    }
    static async Task<JsonDocument> State(HttpClient client, Guid roomId) =>
        JsonDocument.Parse(await client.GetStringAsync($"/api/duel/rooms/{roomId}"));
    static SeatInput Input(JsonElement duel, DuelCommandKind kind) => new() {
        Revision = duel.GetProperty("revision").GetInt64(), ActionToken = duel.GetProperty("actions").EnumerateArray()
            .Single(a => a.GetProperty("kind").GetInt32() == (int)kind).GetProperty("actionToken").GetString()! };
    static Task Send(WebSocket socket, Guid requestId, SeatInput input, CancellationToken token) =>
        socket.SendAsync(new ArraySegment<byte>(JsonSerializer.SerializeToUtf8Bytes(new { kind = "command", requestId, input }, Json)),
            WebSocketMessageType.Text, true, token);
    static async Task<JsonDocument> Receipt(WebSocket socket, Guid requestId, CancellationToken token)
    {
        var bytes = new byte[65536];
        while (true)
        {
            using var body = new MemoryStream();
            WebSocketReceiveResult part;
            do { part = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), token); body.Write(bytes, 0, part.Count); }
            while (!part.EndOfMessage);
            var notice = JsonDocument.Parse(body.ToArray());
            if (notice.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.Object
                && result.GetProperty("requestId").GetGuid() == requestId) return notice;
            notice.Dispose();
        }
    }
}
