using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using AChen.Backend.Api.Features.Duels;
using AChen.Duel.Core;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AChen.Backend.Api.Tests;

// The only injected game input is the server's deterministic seed and first seat. Rules and support gate remain production defaults.
public sealed class DuelChainFlowEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Signed_name_query_requires_its_seats_current_action_token_and_filters_only_its_deck()
    {
        var main = Deck(new[] { "65681983", "89631139", "48800175" });
        var opponent = Deck(new[] { "49299410", "14558127" });
        var seed = FindSeed(main, opponent, new[] { "65681983" }, Array.Empty<string>());
        using var app = Application(seed);
        using var host = await Register(app); using var guest = await Register(app);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var id = await Room(host, guest);
        using var hostSocket = await Connect(app, host, id, timeout.Token); using var guestSocket = await Connect(app, guest, id, timeout.Token);
        var clients = new[] { host, guest }; var sockets = new[] { hostSocket, guestSocket };
        await Prepare(host, id, main); await Prepare(guest, id, opponent); await ToMain(clients, sockets, id, timeout.Token);
        using var state = await State(host, id); var duel = state.RootElement.GetProperty("duel");
        var action = Action(duel, "65681983.1");
        string route = $"/api/duel/rooms/{id}/names?actionToken={action.GetProperty("actionToken").GetString()}&query=&offset=0";
        var response = await host.GetAsync(route); response.EnsureSuccessStatusCode();
        var page = (await response.Content.ReadFromJsonAsync<DuelNamePage>(Json))!;
        Assert.DoesNotContain(page.Items, name => name.NameId == "49299410");
        Assert.True(page.Items.Count <= 100);
        var replayStart = app.Services.GetRequiredService<DuelRoomService>().CaptureReplay(id).Start;
        var authority = new DuelEngine(DuelCardCatalog.CreateDefault(), replayStart);
        var validNames = authority.State.Players[0].Deck.Select(card => authority.State.Cards.Single(c => c.InstanceId == card).CurrentNameId).ToHashSet();
        Assert.All(page.Items, name => Assert.Contains(name.NameId, validNames));
        Assert.Equal(System.Net.HttpStatusCode.Conflict, (await guest.GetAsync(route)).StatusCode);
    }

    [Fact]
    public async Task A_signed_live_connection_receives_an_authority_timeout_and_its_private_replay_records_the_system_step()
    {
        var clock = new LiveRoomClock();
        using var app = Application(73, clock);
        using var host = await Register(app); using var guest = await Register(app);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var id = await Room(host, guest);
        using var hostSocket = await Connect(app, host, id, timeout.Token);
        using var guestSocket = await Connect(app, guest, id, timeout.Token);
        var deck = Deck(new[] { "89631139", "38120068" });
        await Prepare(host, id, deck); await Prepare(guest, id, deck);
        var service = app.Services.GetRequiredService<DuelRoomService>();
        clock.Advance(TimeSpan.FromSeconds(179)); service.Tick();
        using var almost = await State(host, id);
        Assert.Equal(1, almost.RootElement.GetProperty("remainingSeconds")[0].GetDouble());
        Assert.Equal(180, almost.RootElement.GetProperty("remainingSeconds")[1].GetDouble());
        clock.Advance(TimeSpan.FromSeconds(1)); service.Tick();
        using var ended = await State(host, id);
        Assert.Equal("TIMEOUT", ended.RootElement.GetProperty("duel").GetProperty("endReason").GetString());
        Assert.Equal(1, ended.RootElement.GetProperty("duel").GetProperty("winner").GetInt32());
        var replay = service.CaptureReplay(id);
        Assert.Equal(DuelCommandKind.Timeout, Assert.Single(replay.Entries).Command.Kind);
        Assert.True(DuelReplayRunner.Run(DuelCardCatalog.CreateDefault(), replay).Verified);
    }

    sealed class LiveRoomClock : TimeProvider
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan elapsed) => now += elapsed;
    }

    [Fact]
    public async Task Real_signed_clients_pay_cost_chain_ash_recover_a_pending_search_and_reject_its_old_choice()
    {
        var main = Deck(new[] { "48800175", "89631139", "00213326", "40044918", "06853254" });
        var opponent = Deck(new[] { "14558127" });
        var seed = FindSeed(main, opponent, new[] { "48800175", "89631139", "00213326", "06853254" }, new[] { "14558127" });
        using var app = Application(seed);
        using var host = await Register(app); using var guest = await Register(app);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var roomId = await Room(host, guest);
        var sockets = new[] { await Connect(app, host, roomId, timeout.Token), await Connect(app, guest, roomId, timeout.Token) };
        var clients = new[] { host, guest };
        try
        {
            await Prepare(host, roomId, main); await Prepare(guest, roomId, opponent);
            await ToMain(clients, sockets, roomId, timeout.Token);
            using var before = await State(host, roomId);
            var duel = before.RootElement.GetProperty("duel");
            var melody = Action(duel, "48800175.1");
            var discard = duel.GetProperty("cards").EnumerateArray().First(c => c.GetProperty("definitionId").GetString() == "89631139"
                && c.GetProperty("zone").GetInt32() == (int)DuelZone.Hand).GetProperty("viewCardId").GetString();
            using var paid = await Rpc(sockets[0], Input(duel, melody, selections: new[] { discard! }), timeout.Token);
            Assert.True(paid.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean());
            using var response = await State(guest, roomId);
            var responding = response.RootElement.GetProperty("duel");
            using var ash = await Rpc(sockets[1], Input(responding, Action(responding, "14558127.1")), timeout.Token);
            await Pass(clients, sockets, roomId, timeout.Token); await Pass(clients, sockets, roomId, timeout.Token);
            using var resolved = await State(host, roomId);
            Assert.Equal(JsonValueKind.Null, resolved.RootElement.GetProperty("duel").GetProperty("decision").ValueKind);
            Assert.Contains(resolved.RootElement.GetProperty("duel").GetProperty("cards").EnumerateArray(), c =>
                c.GetProperty("definitionId").GetString() == "89631139" && c.GetProperty("zone").GetInt32() == (int)DuelZone.Graveyard);
            await Open(clients, sockets, roomId, timeout.Token);
            using var source = await State(host, roomId);
            var sourceView = source.RootElement.GetProperty("duel");
            using var activated = await Rpc(sockets[0], Input(sourceView, Action(sourceView, "00213326.1")), timeout.Token);
            await Pass(clients, sockets, roomId, timeout.Token); await Pass(clients, sockets, roomId, timeout.Token);
            using var decision = await State(host, roomId);
            var decisionView = decision.RootElement.GetProperty("duel");
            Assert.Equal((int)DecisionKind.ChooseCards, decisionView.GetProperty("decision").GetProperty("kind").GetInt32());
            var option = decisionView.GetProperty("decision").GetProperty("options").EnumerateArray()
                .First(o => o.GetProperty("definitionId").GetString() == "40044918").GetProperty("optionToken").GetString();
            var choice = new SeatInput { Revision = decisionView.GetProperty("revision").GetInt64(), OptionTokens = new[] { option! } };
            await sockets[0].CloseAsync(WebSocketCloseStatus.NormalClosure, "reconnect", timeout.Token);
            using var paused = await State(guest, roomId);
            Assert.True(paused.RootElement.GetProperty("paused").GetBoolean());
            sockets[0] = await Connect(app, host, roomId, timeout.Token);
            using var resumed = await State(host, roomId);
            Assert.Equal(choice.Revision, resumed.RootElement.GetProperty("duel").GetProperty("revision").GetInt64());
            var requestId = Guid.NewGuid();
            using var answered = await Rpc(sockets[0], choice, timeout.Token, requestId);
            Assert.True(answered.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean());
            using var duplicate = await Rpc(sockets[0], choice, timeout.Token, requestId);
            Assert.Equal(answered.RootElement.GetProperty("result").GetRawText(), duplicate.RootElement.GetProperty("result").GetRawText());
            using var stale = await Rpc(sockets[0], choice, timeout.Token);
            Assert.False(stale.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean());
            Assert.Equal(answered.RootElement.GetProperty("room").GetProperty("duel").GetProperty("revision").GetInt64(),
                stale.RootElement.GetProperty("room").GetProperty("duel").GetProperty("revision").GetInt64());
            await Open(clients, sockets, roomId, timeout.Token);
            using var revivalState = await State(host, roomId); var revivalDuel = revivalState.RootElement.GetProperty("duel");
            var revivalInput = Input(revivalDuel, Action(revivalDuel, "06853254.1"));
            revivalInput.TargetViewCardId = revivalDuel.GetProperty("cards").EnumerateArray().First(c =>
                c.GetProperty("definitionId").GetString() == "89631139" && c.GetProperty("zone").GetInt32() == (int)DuelZone.Graveyard)
                .GetProperty("viewCardId").GetString()!;
            using var reviving = await Rpc(sockets[0], revivalInput, timeout.Token);
            await Pass(clients, sockets, roomId, timeout.Token); await Pass(clients, sockets, roomId, timeout.Token);
            using var zoneState = await State(host, roomId); var zoneDuel = zoneState.RootElement.GetProperty("duel");
            Assert.Equal((int)DecisionKind.ChooseZone, zoneDuel.GetProperty("decision").GetProperty("kind").GetInt32());
            using var zoneChosen = await Rpc(sockets[0], new SeatInput { Revision = zoneDuel.GetProperty("revision").GetInt64(),
                OptionTokens = new[] { zoneDuel.GetProperty("decision").GetProperty("options")[0].GetProperty("optionToken").GetString()! } }, timeout.Token);
            using var positionState = await State(host, roomId); var positionDuel = positionState.RootElement.GetProperty("duel");
            Assert.Equal((int)DecisionKind.ChoosePosition, positionDuel.GetProperty("decision").GetProperty("kind").GetInt32());
            var defense = positionDuel.GetProperty("decision").GetProperty("options").EnumerateArray().Single(o => o.GetProperty("label").GetString() == "守备表示");
            using var positioned = await Rpc(sockets[0], new SeatInput { Revision = positionDuel.GetProperty("revision").GetInt64(),
                OptionTokens = new[] { defense.GetProperty("optionToken").GetString()! } }, timeout.Token);
            Assert.Contains(positioned.RootElement.GetProperty("room").GetProperty("duel").GetProperty("cards").EnumerateArray(), c =>
                c.GetProperty("definitionId").GetString() == "89631139" && c.GetProperty("zone").GetInt32() == (int)DuelZone.Monster
                && c.GetProperty("position").GetInt32() == (int)CardPosition.FaceUpDefense);
            await FinishAndReplay(app, host, sockets[0], roomId, timeout.Token);
        }
        finally { foreach (var socket in sockets) socket.Dispose(); }
    }

    [Fact]
    public async Task Real_signed_clients_choose_an_activation_mode_and_complete_a_normal_summon_trigger_search()
    {
        var main = Deck(new[] { "00213326", "25311006", "08240199", "97268402", "40044918" });
        var opponent = Deck(new[] { "14558127" });
        var seed = FindSeed(main, opponent, new[] { "00213326", "25311006", "08240199" }, new[] { "14558127" });
        using var app = Application(seed);
        using var host = await Register(app); using var guest = await Register(app);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var roomId = await Room(host, guest);
        var sockets = new[] { await Connect(app, host, roomId, timeout.Token), await Connect(app, guest, roomId, timeout.Token) };
        var clients = new[] { host, guest };
        try
        {
            await Prepare(host, roomId, main); await Prepare(guest, roomId, opponent);
            await ToMain(clients, sockets, roomId, timeout.Token);
            using var own = await State(host, roomId); var ownDuel = own.RootElement.GetProperty("duel");
            using var emergency = await Rpc(sockets[0], Input(ownDuel, Action(ownDuel, "00213326.1")), timeout.Token);
            using var counter = await State(guest, roomId); var counterDuel = counter.RootElement.GetProperty("duel");
            using var ash = await Rpc(sockets[1], Input(counterDuel, Action(counterDuel, "14558127.1")), timeout.Token);
            await Pass(clients, sockets, roomId, timeout.Token); await Pass(clients, sockets, roomId, timeout.Token);
            await Open(clients, sockets, roomId, timeout.Token);
            using var modeState = await State(host, roomId); var modeDuel = modeState.RootElement.GetProperty("duel");
            int handBefore = modeDuel.GetProperty("players")[0].GetProperty("handCount").GetInt32();
            var drawMode = modeDuel.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("abilityId").GetString() == "25311006.1"
                && a.GetProperty("label").GetString()!.Contains("抽"));
            using var talent = await Rpc(sockets[0], Input(modeDuel, drawMode), timeout.Token);
            await Pass(clients, sockets, roomId, timeout.Token); await Pass(clients, sockets, roomId, timeout.Token);
            using var drawn = await State(host, roomId);
            Assert.Equal(handBefore + 1, drawn.RootElement.GetProperty("duel").GetProperty("players")[0].GetProperty("handCount").GetInt32());
            await Open(clients, sockets, roomId, timeout.Token);
            using var summonState = await State(host, roomId); var summonDuel = summonState.RootElement.GetProperty("duel");
            string sageHandle = summonDuel.GetProperty("cards").EnumerateArray().First(c => c.GetProperty("definitionId").GetString() == "08240199"
                && c.GetProperty("zone").GetInt32() == (int)DuelZone.Hand).GetProperty("viewCardId").GetString()!;
            var normal = summonDuel.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("kind").GetInt32() == (int)DuelCommandKind.NormalSummon
                && a.GetProperty("sourceViewCardId").GetString() == sageHandle);
            using var summoned = await Rpc(sockets[0], Input(summonDuel, normal), timeout.Token);
            using var offered = await State(host, roomId); var offerDuel = offered.RootElement.GetProperty("duel");
            Assert.Equal((int)DecisionKind.YesNo, offerDuel.GetProperty("decision").GetProperty("kind").GetInt32());
            var yes = offerDuel.GetProperty("decision").GetProperty("options").EnumerateArray().Single(o => o.GetProperty("label").GetString() == "发动");
            using var confirmed = await Rpc(sockets[0], new SeatInput { Revision = offerDuel.GetProperty("revision").GetInt64(),
                OptionTokens = new[] { yes.GetProperty("optionToken").GetString()! } }, timeout.Token);
            await Pass(clients, sockets, roomId, timeout.Token); await Pass(clients, sockets, roomId, timeout.Token);
            using var search = await State(host, roomId); var searchDuel = search.RootElement.GetProperty("duel");
            var selected = searchDuel.GetProperty("decision").GetProperty("options")[0].GetProperty("optionToken").GetString()!;
            using var searched = await Rpc(sockets[0], new SeatInput { Revision = searchDuel.GetProperty("revision").GetInt64(), OptionTokens = new[] { selected } }, timeout.Token);
            Assert.Equal(JsonValueKind.Null, searched.RootElement.GetProperty("room").GetProperty("duel").GetProperty("decision").ValueKind);
            await FinishAndReplay(app, host, sockets[0], roomId, timeout.Token);
        }
        finally { foreach (var socket in sockets) socket.Dispose(); }
    }

    WebApplicationFactory<Program> Application(ulong seed, TimeProvider? roomClock = null) => factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
    {
        services.RemoveAll<DuelRoomService>();
        services.AddSingleton(provider => new DuelRoomService(roomClock ?? provider.GetRequiredService<TimeProvider>(),
            new DuelRoomCardSupport(CardRuleCatalog.CreateDefault(DuelCardCatalog.CreateDefault())), new FixedStart(seed)));
    }));
    sealed class FixedStart(ulong seed) : IDuelRoomStartSource
    {
        public DuelStartRecord Create(string[][] mainDecks, string[][] extraDecks) => new()
        { MainDecks = mainDecks, ExtraDecks = extraDecks, Seed = seed, FirstPlayer = 0 };
    }
    static string[] Deck(string[] required)
    {
        var printed = DuelCardCatalog.CreateDefault(); var rules = CardRuleCatalog.CreateDefault(printed);
        var prefix = required.SelectMany(id => Enumerable.Repeat(id, printed.Get(id).MaxCopies)).ToArray();
        return prefix.Concat(printed.Cards.Where(c => !c.IsExtra && c.MaxCopies > 0 && rules.Get(c.CardId).Support.IsComplete
            && !required.Contains(c.CardId)).SelectMany(c => Enumerable.Repeat(c.CardId, c.MaxCopies))).Take(40).ToArray();
    }
    static ulong FindSeed(string[] first, string[] second, string[] hand0, string[] hand1)
    {
        var catalog = DuelCardCatalog.CreateDefault();
        for (ulong seed = 1; seed <= 20000; seed++)
        {
            var engine = new DuelEngine(catalog, new DuelStartRecord { MainDecks = new[] { first, second }, Seed = seed, FirstPlayer = 0 });
            if (hand0.All(id => engine.State.Cards.Any(c => c.Owner == 0 && c.Zone == DuelZone.Hand && c.DefinitionId == id))
                && hand1.All(id => engine.State.Cards.Any(c => c.Owner == 1 && c.Zone == DuelZone.Hand && c.DefinitionId == id))) return seed;
        }
        throw new InvalidOperationException("No seeded opening satisfies this deterministic gameplay fixture.");
    }
    static async Task<HttpClient> Register(WebApplicationFactory<Program> app)
    {
        var client = app.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new { username = "Chain" + Guid.NewGuid().ToString("N")[..12], password = "correct-horse-42" });
        response.EnsureSuccessStatusCode(); using var parsed = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", parsed.RootElement.GetProperty("accessToken").GetString());
        return client;
    }
    static async Task<Guid> Room(HttpClient host, HttpClient guest)
    {
        var response = await host.PostAsync("/api/duel/rooms", null); response.EnsureSuccessStatusCode();
        var room = (await response.Content.ReadFromJsonAsync<DuelRoomView>())!;
        (await guest.PostAsJsonAsync("/api/duel/rooms/join", new { room.Code })).EnsureSuccessStatusCode(); return room.Id;
    }
    static async Task Prepare(HttpClient client, Guid roomId, string[] main)
    {
        (await client.PutAsJsonAsync($"/api/duel/rooms/{roomId}/deck", new DuelDeckRequest(main, Array.Empty<string>()))).EnsureSuccessStatusCode();
        (await client.PostAsJsonAsync($"/api/duel/rooms/{roomId}/ready", new { ready = true })).EnsureSuccessStatusCode();
    }
    static async Task<WebSocket> Connect(WebApplicationFactory<Program> app, HttpClient account, Guid id, CancellationToken token)
    {
        var client = app.Server.CreateWebSocketClient(); client.ConfigureRequest = request => request.Headers["Authorization"] = account.DefaultRequestHeaders.Authorization!.ToString();
        return await client.ConnectAsync(new Uri($"ws://localhost/api/duel/rooms/{id}/socket?protocolVersion=1"), token);
    }
    static async Task<JsonDocument> State(HttpClient client, Guid id) => JsonDocument.Parse(await client.GetStringAsync($"/api/duel/rooms/{id}"));
    static JsonElement Action(JsonElement duel, string abilityId) => duel.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("abilityId").GetString() == abilityId);
    static SeatInput Input(JsonElement duel, JsonElement action, string[]? selections = null) => new()
    {
        Revision = duel.GetProperty("revision").GetInt64(), ActionToken = action.GetProperty("actionToken").GetString()!,
        Slot = action.GetProperty("slots").GetArrayLength() > 0 ? action.GetProperty("slots")[0].GetInt32() : 0,
        Position = action.GetProperty("positions").GetArrayLength() > 0 ? (CardPosition)action.GetProperty("positions")[0].GetInt32() : CardPosition.FaceUpAttack,
        SelectionViewCardIds = selections ?? Array.Empty<string>()
    };
    static async Task Pass(HttpClient[] accounts, WebSocket[] sockets, Guid id, CancellationToken token)
    {
        using var state = await State(accounts[0], id); int seat = state.RootElement.GetProperty("duel").GetProperty("waitingSeat").GetInt32();
        using var own = await State(accounts[seat], id); var duel = own.RootElement.GetProperty("duel");
        using var result = await Rpc(sockets[seat], Input(duel, duel.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("kind").GetInt32() == (int)DuelCommandKind.Pass)), token);
        Assert.True(result.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean());
    }
    static async Task ToMain(HttpClient[] accounts, WebSocket[] sockets, Guid id, CancellationToken token)
    { for (int i = 0; i < 4; i++) await Pass(accounts, sockets, id, token); }
    static async Task Open(HttpClient[] accounts, WebSocket[] sockets, Guid id, CancellationToken token)
    {
        using var state = await State(accounts[0], id);
        if (state.RootElement.GetProperty("duel").GetProperty("window").GetInt32() == (int)TimingWindow.Open) return;
        await Pass(accounts, sockets, id, token); await Pass(accounts, sockets, id, token);
    }
    static async Task<JsonDocument> Rpc(WebSocket socket, SeatInput input, CancellationToken token, Guid? request = null)
    {
        var requestId = request ?? Guid.NewGuid();
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new { kind = "command", requestId, input }, Json);
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        while (true)
        {
            using var body = new MemoryStream(); var buffer = new byte[65536]; WebSocketReceiveResult chunk;
            do { chunk = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), token); body.Write(buffer, 0, chunk.Count); } while (!chunk.EndOfMessage);
            var message = JsonDocument.Parse(body.ToArray());
            if (message.RootElement.TryGetProperty("result", out var receipt) && receipt.ValueKind == JsonValueKind.Object
                && receipt.GetProperty("requestId").GetGuid() == requestId) return message;
            message.Dispose();
        }
    }
    static async Task FinishAndReplay(WebApplicationFactory<Program> app, HttpClient account, WebSocket socket, Guid id, CancellationToken token)
    {
        using var state = await State(account, id); var duel = state.RootElement.GetProperty("duel");
        var surrender = duel.GetProperty("actions").EnumerateArray().Single(a => a.GetProperty("kind").GetInt32() == (int)DuelCommandKind.Surrender);
        using var ended = await Rpc(socket, Input(duel, surrender), token);
        Assert.Equal("Finished", ended.RootElement.GetProperty("room").GetProperty("status").GetString());
        var replay = app.Services.GetRequiredService<DuelRoomService>().CaptureReplay(id);
        Assert.True(DuelReplayRunner.Run(DuelCardCatalog.CreateDefault(), replay).Verified);
        Assert.Contains(replay.Entries, e => e.Command.Kind == DuelCommandKind.Activate);
        Assert.Contains(replay.Entries, e => e.Command.Kind == DuelCommandKind.Answer);
    }
}
