using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AChen.Backend.Api.Features.Duels;
using AChen.Duel.Core;
using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AChen.Backend.Api.Tests;

public sealed class DuelRoomEndpointsTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    readonly WebApplicationFactory<Program> duelFactory = factory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DuelRoomService>();
            services.AddSingleton(provider => DuelRoomFixtures.Create(provider.GetRequiredService<TimeProvider>()));
        }));
    [Fact]
    public async Task Unsupported_protocol_is_rejected_before_opening_a_room_socket()
    {
        using var host = await RegisterAsync();
        var response = await host.PostAsync("/api/duel/rooms", null);
        var room = (await response.Content.ReadFromJsonAsync<DuelRoomView>())!;
        var incompatible = await host.GetAsync($"/api/duel/rooms/{room.Id}/socket?protocolVersion=2");
        Assert.Equal(HttpStatusCode.Conflict, incompatible.StatusCode);
        using var failure = JsonDocument.Parse(await incompatible.Content.ReadAsStringAsync());
        Assert.Equal("DUEL_PROTOCOL_MISMATCH", failure.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Jwt_accounts_create_join_and_leave_a_room_without_friendship_or_inventory()
    {
        using var anonymous = duelFactory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync("/api/duel/rooms", null)).StatusCode);
        using var host = await RegisterAsync();
        using var guest = await RegisterAsync();
        var createdResponse = await host.PostAsync("/api/duel/rooms", null);
        createdResponse.EnsureSuccessStatusCode();
        var room = (await createdResponse.Content.ReadFromJsonAsync<DuelRoomView>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await guest.GetAsync($"/api/duel/rooms/{room.Id}")).StatusCode);
        var joined = await guest.PostAsJsonAsync("/api/duel/rooms/join", new { room.Code });
        joined.EnsureSuccessStatusCode();
        Assert.Equal(1, (await joined.Content.ReadFromJsonAsync<DuelRoomView>())!.LocalSeat);
        Assert.Equal(HttpStatusCode.NoContent, (await guest.DeleteAsync($"/api/duel/rooms/{room.Id}")).StatusCode);
        Assert.Single((await host.GetFromJsonAsync<DuelRoomView>($"/api/duel/rooms/{room.Id}"))!.Players);
    }

    async Task<HttpClient> RegisterAsync()
    {
        var client = duelFactory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/auth/register", new
        { username = "Duel" + Guid.NewGuid().ToString("N")[..12], password = "correct-horse-42" });
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer",
            json.RootElement.GetProperty("accessToken").GetString());
        return client;
    }

    [Fact]
    public async Task Free_decks_can_start_a_duel_for_new_accounts_with_no_owned_cards()
    {
        using var host = await RegisterAsync();
        using var guest = await RegisterAsync();
        var response = await host.PostAsync("/api/duel/rooms", null);
        var room = (await response.Content.ReadFromJsonAsync<DuelRoomView>())!;
        (await guest.PostAsJsonAsync("/api/duel/rooms/join", new { room.Code })).EnsureSuccessStatusCode();
        var deck = new DuelDeckRequest(DuelCardCatalog.CreateDefault().Cards.Where(card => !card.IsExtra)
            .SelectMany(card => Enumerable.Repeat(card.CardId, card.MaxCopies)).Take(40).ToArray(), Array.Empty<string>());

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var hostSocket = await ConnectAsync(host, room.Id, timeout.Token);
        using var guestSocket = await ConnectAsync(guest, room.Id, timeout.Token);

        (await host.PutAsJsonAsync($"/api/duel/rooms/{room.Id}/deck", deck)).EnsureSuccessStatusCode();
        (await guest.PutAsJsonAsync($"/api/duel/rooms/{room.Id}/deck", deck)).EnsureSuccessStatusCode();
        (await host.PostAsJsonAsync($"/api/duel/rooms/{room.Id}/ready", new { ready = true })).EnsureSuccessStatusCode();
        var started = await guest.PostAsJsonAsync($"/api/duel/rooms/{room.Id}/ready", new { ready = true });
        started.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await started.Content.ReadAsStringAsync());
        Assert.Equal("Running", json.RootElement.GetProperty("status").GetString());
        Assert.Equal(5, json.RootElement.GetProperty("duel").GetProperty("players")[1].GetProperty("handCount").GetInt32());
    }

    [Fact]
    public async Task Authenticated_websocket_pushes_room_changes_for_its_bound_account()
    {
        using var host = await RegisterAsync();
        using var guest = await RegisterAsync();
        var created = await host.PostAsync("/api/duel/rooms", null);
        var room = (await created.Content.ReadFromJsonAsync<DuelRoomView>())!;
        var client = duelFactory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["Authorization"] = host.DefaultRequestHeaders.Authorization!.ToString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await client.ConnectAsync(new Uri($"ws://localhost/api/duel/rooms/{room.Id}/socket?protocolVersion=1"), timeout.Token);
        using var initial = await ReadMessageAsync(socket, timeout.Token);
        Assert.Equal(0, initial.RootElement.GetProperty("room").GetProperty("localSeat").GetInt32());

        (await guest.PostAsJsonAsync("/api/duel/rooms/join", new { room.Code })).EnsureSuccessStatusCode();
        using var update = await ReadMessageAsync(socket, timeout.Token);
        Assert.Equal(2, update.RootElement.GetProperty("room").GetProperty("players").GetArrayLength());
        Assert.True(update.RootElement.GetProperty("room").GetProperty("sequence").GetInt64()
            > initial.RootElement.GetProperty("room").GetProperty("sequence").GetInt64());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    static async Task<JsonDocument> ReadMessageAsync(WebSocket socket, CancellationToken token)
    {
        var bytes = new byte[65536];
        using var message = new MemoryStream();
        WebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), token);
            message.Write(bytes, 0, received.Count);
        } while (!received.EndOfMessage);
        return JsonDocument.Parse(message.ToArray());
    }

    async Task<WebSocket> ConnectAsync(HttpClient account, Guid roomId, CancellationToken token)
    {
        var client = duelFactory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["Authorization"] = account.DefaultRequestHeaders.Authorization!.ToString();
        return await client.ConnectAsync(new Uri($"ws://localhost/api/duel/rooms/{roomId}/socket?protocolVersion=1"), token);
    }

    [Fact]
    public async Task Websocket_commands_use_opaque_actions_and_retry_without_executing_twice()
    {
        using var host = await RegisterAsync();
        using var guest = await RegisterAsync();
        var created = await host.PostAsync("/api/duel/rooms", null);
        var room = (await created.Content.ReadFromJsonAsync<DuelRoomView>())!;
        (await guest.PostAsJsonAsync("/api/duel/rooms/join", new { room.Code })).EnsureSuccessStatusCode();
        var deck = new DuelDeckRequest(DuelCardCatalog.CreateDefault().Cards.Where(card => !card.IsExtra)
            .SelectMany(card => Enumerable.Repeat(card.CardId, card.MaxCopies)).Take(40).ToArray(), Array.Empty<string>());
        using var peersTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var hostSocket = await ConnectAsync(host, room.Id, peersTimeout.Token);
        using var guestSocket = await ConnectAsync(guest, room.Id, peersTimeout.Token);
        foreach (var player in new[] { host, guest })
        {
            (await player.PutAsJsonAsync($"/api/duel/rooms/{room.Id}/deck", deck)).EnsureSuccessStatusCode();
            (await player.PostAsJsonAsync($"/api/duel/rooms/{room.Id}/ready", new { ready = true })).EnsureSuccessStatusCode();
        }
        using var state = JsonDocument.Parse(await host.GetStringAsync($"/api/duel/rooms/{room.Id}"));
        var current = state.RootElement.GetProperty("duel").GetProperty("waitingSeat").GetInt32() == 0 ? host : guest;
        var client = duelFactory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["Authorization"] = current.DefaultRequestHeaders.Authorization!.ToString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var socket = await client.ConnectAsync(new Uri($"ws://localhost/api/duel/rooms/{room.Id}/socket?protocolVersion=1"), timeout.Token);
        using var initial = await ReadMessageAsync(socket, timeout.Token);
        var duel = initial.RootElement.GetProperty("room").GetProperty("duel");
        var pass = duel.GetProperty("actions").EnumerateArray().Single(action => action.GetProperty("kind").GetInt32() == (int)DuelCommandKind.Pass);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(new
        { kind = "command", requestId = Guid.NewGuid(), input = new { revision = duel.GetProperty("revision").GetInt64(), actionToken = pass.GetProperty("actionToken").GetString() } });

        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token);
        using var first = await ReadMessageAsync(socket, timeout.Token);
        Assert.True(first.RootElement.GetProperty("result").GetProperty("accepted").GetBoolean());
        await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, timeout.Token);
        using var duplicate = await ReadMessageAsync(socket, timeout.Token);
        Assert.Equal(first.RootElement.GetProperty("result").GetRawText(), duplicate.RootElement.GetProperty("result").GetRawText());
        Assert.Equal(0, duplicate.RootElement.GetProperty("events").GetArrayLength());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }

    [Fact]
    public async Task Websocket_ping_returns_a_snapshot_without_creating_a_rule_step()
    {
        using var host = await RegisterAsync();
        var created = await host.PostAsync("/api/duel/rooms", null);
        var room = (await created.Content.ReadFromJsonAsync<DuelRoomView>())!;
        var client = duelFactory.Server.CreateWebSocketClient();
        client.ConfigureRequest = request => request.Headers["Authorization"] = host.DefaultRequestHeaders.Authorization!.ToString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var socket = await client.ConnectAsync(new Uri($"ws://localhost/api/duel/rooms/{room.Id}/socket?protocolVersion=1"), timeout.Token);
        using var initial = await ReadMessageAsync(socket, timeout.Token);
        await socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"kind\":\"ping\"}")), WebSocketMessageType.Text, true, timeout.Token);
        using var pong = await ReadMessageAsync(socket, timeout.Token);
        Assert.Equal(initial.RootElement.GetProperty("room").GetProperty("sequence").GetInt64(),
            pong.RootElement.GetProperty("room").GetProperty("sequence").GetInt64());
        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", timeout.Token);
    }
}
