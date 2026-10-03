using System.Net.WebSockets;
using System.Text.Json;
using AChen.Backend.Api.Infrastructure;
using AChen.Duel.Core;

namespace AChen.Backend.Api.Features.Duels;

public static class DuelRoomSocket
{
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { IncludeFields = true };

    public static async Task RunAsync(Guid id, HttpContext context, DuelRoomService service)
    {
        var userId = DuelRoomEndpoints.UserId(context);
        if (!int.TryParse(context.Request.Query["protocolVersion"], out var version)
            || version != DuelRulePackage.CurrentProtocolVersion)
            throw new ApiException(409, "DUEL_PROTOCOL_MISMATCH", "对局同步协议版本不一致");
        service.Get(userId, id);
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }
        using var socket = await context.WebSockets.AcceptWebSocketAsync();
        var connection = service.Connect(userId, id);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, connection.Stopped);
        var send = SendAsync(socket, connection, lifetime.Token);
        var receive = ReceiveAsync(socket, service, userId, id, connection.Id, lifetime.Token);
        try
        {
            await Task.WhenAny(send, receive);
            lifetime.Cancel();
            try { await Task.WhenAll(send, receive); }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            catch (WebSocketException) { }
            if (socket.State == WebSocketState.CloseReceived)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "closed", CancellationToken.None);
        }
        finally
        {
            service.Disconnect(userId, id, connection.Id);
        }
    }

    static async Task SendAsync(WebSocket socket, DuelRoomConnection connection, CancellationToken token)
    {
        await foreach (var update in connection.Updates.ReadAllAsync(token))
        {
            var bytes = JsonSerializer.SerializeToUtf8Bytes(update, Json);
            await socket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, token);
        }
    }

    static async Task ReceiveAsync(WebSocket socket, DuelRoomService service, Guid userId, Guid roomId,
        Guid connectionId, CancellationToken token)
    {
        var bytes = new byte[4096];
        while (!token.IsCancellationRequested)
        {
            using var body = new MemoryStream();
            using var idle = CancellationTokenSource.CreateLinkedTokenSource(token);
            idle.CancelAfter(TimeSpan.FromSeconds(15));
            WebSocketReceiveResult received;
            do
            {
                try { received = await socket.ReceiveAsync(new ArraySegment<byte>(bytes), idle.Token); }
                catch (OperationCanceledException) when (!token.IsCancellationRequested) { return; }
                if (received.MessageType == WebSocketMessageType.Close) return;
                if (received.MessageType != WebSocketMessageType.Text || body.Length + received.Count > 16 * 1024)
                {
                    service.RejectMessage(userId, roomId, connectionId, Guid.Empty, "INVALID_DUEL_MESSAGE");
                    return;
                }
                body.Write(bytes, 0, received.Count);
            } while (!received.EndOfMessage);
            Guid requestId = Guid.Empty;
            try
            {
                var message = JsonSerializer.Deserialize<DuelSocketMessage>(body.ToArray(), Json);
                if (message == null) throw new JsonException();
                requestId = message.RequestId;
                if (message.Kind == "ping")
                {
                    service.Ping(userId, roomId, connectionId);
                    continue;
                }
                if (message.Kind != "command") throw new JsonException();
                service.Submit(userId, roomId, new DuelInputRequest(message.RequestId, message.Input!), connectionId);
            }
            catch (JsonException) { service.RejectMessage(userId, roomId, connectionId, requestId, "INVALID_DUEL_MESSAGE"); }
            catch (ApiException error) { service.RejectMessage(userId, roomId, connectionId, requestId, error.Code); }
        }
    }

    sealed class DuelSocketMessage
    {
        public string Kind { get; set; } = "";
        public Guid RequestId { get; set; }
        public SeatInput? Input { get; set; }
    }
}
