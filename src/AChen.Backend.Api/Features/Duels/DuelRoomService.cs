using System.Security.Cryptography;
using System.Text.Json;
using AChen.Backend.Api.Infrastructure;
using AChen.Duel.Core;

namespace AChen.Backend.Api.Features.Duels;

/// <summary>账号绑定的双人好友房间。房间码只定位房间，账号决定席位。</summary>
public sealed class DuelRoomService
{
    readonly TimeProvider clock;
    readonly IDuelRoomCardSupport support;
    readonly IDuelRoomStartSource starts;
    readonly object gate = new();
    readonly Dictionary<Guid, Room> rooms = new();
    readonly Dictionary<string, Room> codes = new(StringComparer.Ordinal);
    readonly Dictionary<Guid, Room> memberships = new();
    readonly DuelCardCatalog catalog = DuelCardCatalog.CreateDefault();
    readonly CardNameCatalog names = CardNameCatalog.CreateDefault();

    public DuelRoomService(TimeProvider clock)
        : this(clock, new DuelRoomCardSupport(CardRuleCatalog.CreateDefault(DuelCardCatalog.CreateDefault()))) { }
    public DuelRoomService(TimeProvider clock, IDuelRoomCardSupport support)
        : this(clock, support, new DuelRoomStartSource()) { }
    public DuelRoomService(TimeProvider clock, IDuelRoomCardSupport support, IDuelRoomStartSource starts)
    { this.clock = clock; this.support = support; this.starts = starts; }

    public DuelRoomView Create(Guid userId)
    {
        Room room;
        lock (gate)
        {
            if (!memberships.TryGetValue(userId, out room!))
            {
                var code = NewCode();
                while (codes.ContainsKey(code)) code = NewCode();
                room = new Room { Code = code, CreatedAt = clock.GetUtcNow(), LastActivityAt = clock.GetUtcNow() };
                room.Players.Add(userId);
                rooms.Add(room.Id, room); codes.Add(code, room); memberships.Add(userId, room);
            }
        }
        lock (room.Gate) { RequireMember(room, userId); AdvanceClock(room); return View(room, userId); }
    }

    public DuelRoomView Join(Guid userId, string code)
    {
        Room room;
        lock (gate)
            if (!codes.TryGetValue(code, out room!)) throw Error(404, "ROOM_NOT_FOUND", "房间不存在");
        lock (room.Gate)
        {
            RequireExisting(room);
            AdvanceClock(room);
            RequireExisting(room);
            room.LastActivityAt = clock.GetUtcNow();
            if (room.Players.Contains(userId)) { RequireMember(room, userId); return View(room, userId); }
            if (room.Players.Count == 2) throw Error(409, "ROOM_FULL", "房间已满");
            RequireWaiting(room);
            lock (gate)
            {
                if (memberships.ContainsKey(userId)) throw Error(409, "ALREADY_IN_ROOM", "请先离开当前房间");
                memberships.Add(userId, room);
            }
            room.Players.Add(userId);
            room.Sequence++;
            Publish(room);
            return View(room, userId);
        }
    }

    public DuelRoomView Get(Guid userId, Guid roomId)
    {
        var room = Find(roomId);
        lock (room.Gate)
        {
            RequireMember(room, userId);
            AdvanceClock(room);
            return View(room, userId);
        }
    }

    public void Leave(Guid userId, Guid roomId)
    {
        var room = Find(roomId);
        lock (room.Gate)
        {
            RequireMember(room, userId);
            AdvanceClock(room);
            if (room.Engine != null)
            {
                if (!room.Engine.State.Finished)
                {
                    var result = ApplyAndRecord(room, new DuelCommand { Kind = DuelCommandKind.Surrender, Player = room.Players.IndexOf(userId) });
                    room.Sequence++;
                    Complete(room);
                    foreach (var player in room.Players) Send(room, player, Notice(room, player, result.Events, null));
                }
                room.Departed.Add(userId);
                lock (gate) memberships.Remove(userId);
                if (room.Connections.Remove(userId, out var departedConnection)) departedConnection.Stop();
                return;
            }
            if (room.Players[0] == userId)
            {
                CloseRoom(room);
                return;
            }
            lock (gate) memberships.Remove(userId);
            room.Players.Remove(userId);
            room.Decks.Remove(userId);
            room.Ready.Remove(userId);
            if (room.Connections.Remove(userId, out var connection)) connection.Stop();
            room.Sequence++;
            Publish(room);
        }
    }

    public DuelRoomView SubmitDeck(Guid userId, Guid roomId, DuelDeckRequest deck)
    {
        var room = Find(roomId);
        lock (room.Gate)
        {
            RequireMember(room, userId);
            AdvanceClock(room);
            RequireWaiting(room);
            room.Decks[userId] = ValidateDeck(deck);
            room.Ready.Remove(userId);
            room.Sequence++;
            Publish(room);
            return View(room, userId);
        }
    }

    public DuelRoomView SetReady(Guid userId, Guid roomId, bool ready)
    {
        var room = Find(roomId);
        lock (room.Gate)
        {
            RequireMember(room, userId);
            AdvanceClock(room);
            RequireWaiting(room);
            if (ready && !room.Decks.ContainsKey(userId)) throw Error(409, "DECK_REQUIRED", "请先提交合法好友构筑");
            if (ready) RequireCardSupport(room.Decks[userId]);
            if (ready) room.Ready.Add(userId); else room.Ready.Remove(userId);
            TryStart(room);
            room.Sequence++;
            Publish(room);
            return View(room, userId);
        }
    }

    static void RequireWaiting(Room room)
    {
        if (room.Status != "Waiting") throw Error(409, "DUEL_ALREADY_STARTED", "对局已开始，构筑与准备状态已经锁定");
    }

    public DuelRoomView ReturnToRoom(Guid userId, Guid roomId)
    {
        var room = Find(roomId);
        lock (room.Gate)
        {
            RequireMember(room, userId);
            AdvanceClock(room);
            if (room.Status != "Finished") throw Error(409, "DUEL_NOT_FINISHED", "对局尚未结束");
            if (room.Departed.Contains(userId)) throw Error(403, "ROOM_FORBIDDEN", "账号已离开此房间");
            foreach (var departed in room.Departed) { room.Players.Remove(departed); room.Decks.Remove(departed); }
            room.Departed.Clear();
            room.Engine = null; room.Replay = null; room.Ready.Clear(); room.Receipts.Clear(); room.DisconnectedAt.Clear();
            room.Projections[0] = new SeatProjection(0); room.Projections[1] = new SeatProjection(1);
            room.Status = "Waiting";
            room.FinishedAt = null;
            room.LastActivityAt = clock.GetUtcNow();
            room.RemainingSeconds[0] = room.RemainingSeconds[1] = 180;
            room.Sequence++;
            Publish(room);
            return View(room, userId);
        }
    }

    void TryStart(Room room)
    {
        if (room.Status != "Waiting" || room.Players.Count != 2 || room.Ready.Count != 2
            || room.Players.Any(player => !room.Connections.ContainsKey(player))) return;
        foreach (var player in room.Players) RequireCardSupport(room.Decks[player]);
        var start = room.RulePackage.Freeze(starts.Create(
            room.Players.Select(id => (string[])room.Decks[id].MainDeck.Clone()).ToArray(),
            room.Players.Select(id => (string[])room.Decks[id].ExtraDeck.Clone()).ToArray()));
        room.Engine = new DuelEngine(catalog, start);
        room.Replay = new DuelReplayRecorder(start, room.Engine.State);
        room.Status = "Running";
        room.ClockAt = clock.GetUtcNow();
    }

    void RequireCardSupport(DuelDeckRequest deck)
    {
        var missing = support.FindMissing(deck.MainDeck.Concat(deck.ExtraDeck));
        if (missing.Count != 0) throw new UnsupportedDuelCardsException(missing);
    }

    public DuelRoomConnection Connect(Guid userId, Guid roomId)
    {
        var room = Find(roomId);
        lock (room.Gate)
        {
            RequireMember(room, userId);
            AdvanceClock(room);
            if (room.Connections.TryGetValue(userId, out var previous)) previous.Stop();
            var connection = new DuelRoomConnection();
            room.Connections[userId] = connection;
            var status = room.Status;
            TryStart(room);
            if (room.Status != status)
            {
                room.Sequence++;
                Publish(room);
                return connection;
            }
            if (room.DisconnectedAt.Remove(userId))
            {
                room.ClockAt = clock.GetUtcNow();
                room.Sequence++;
                Publish(room);
            }
            else connection.Channel.Writer.TryWrite(new DuelRoomNotice(View(room, userId)));
            return connection;
        }
    }

    public DuelRoomNotice Submit(Guid userId, Guid roomId, DuelInputRequest request, Guid? connectionId = null)
    {
        var room = Find(roomId);
        lock (room.Gate)
        {
            RequireMember(room, userId);
            if (connectionId.HasValue && (!room.Connections.TryGetValue(userId, out var active) || active.Id != connectionId.Value))
                throw Error(409, "CONNECTION_REPLACED", "此连接已被新连接替换");
            if (request.RequestId == Guid.Empty || request.Input == null || request.Input.ActionToken == null
                || request.Input.OptionTokens == null || request.Input.SelectionViewCardIds == null
                || request.Input.TargetViewCardId == null || request.Input.NameId == null
                || request.Input.OptionTokens.Any(value => value == null) || request.Input.SelectionViewCardIds.Any(value => value == null))
                throw Error(400, "INVALID_DUEL_INPUT", "对局操作格式无效");
            var key = (userId, request.RequestId);
            var payload = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request.Input)));
            AdvanceClock(room);
            if (room.Receipts.TryGetValue(key, out var previous))
            {
                if (previous.Payload != payload) throw Error(409, "REQUEST_ID_REUSED", "相同请求编号不能提交不同操作");
                var duplicate = new DuelRoomNotice(View(room, userId), previous.Receipt);
                Send(room, userId, duplicate);
                return duplicate;
            }
            if (room.Status != "Running") throw Error(409, "DUEL_NOT_RUNNING", "对局当前不能接受操作");
            var engine = room.Engine!;
            var turnBefore = engine.State.Turn;
            var seat = room.Players.IndexOf(userId);
            DuelStepResult step;
            if (!room.Projections[seat].TryResolveInput(engine.State, request.Input, out var command)
                || command.Kind is DuelCommandKind.Timeout or DuelCommandKind.DisconnectTimeout or DuelCommandKind.Abort)
                step = new DuelStepResult { Error = "STALE_OR_INVALID_INPUT", Revision = engine.State.Revision };
            else
            {
                if (room.DisconnectedAt.Count != 0 && command.Kind != DuelCommandKind.Surrender)
                    throw Error(409, "DUEL_PAUSED", "等待对手重新连接");
                step = ApplyAndRecord(room, command);
            }
            if (engine.State.Turn != turnBefore)
                room.RemainingSeconds[0] = room.RemainingSeconds[1] = 180;
            if (step.Accepted) room.Sequence++;
            if (engine.State.Finished) Complete(room);
            var receipt = new DuelCommandReceipt(request.RequestId, step.Accepted, step.Error, room.Sequence, step.Revision);
            room.Receipts.Add(key, (payload, receipt));
            foreach (var player in room.Players)
                if (step.Accepted || player == userId)
                    Send(room, player, Notice(room, player, step.Events, player == userId ? receipt : null));
            return Notice(room, userId, step.Events, receipt);
        }
    }

    static DuelRoomNotice Notice(Room room, Guid userId, IEnumerable<DuelEvent> events, DuelCommandReceipt? receipt) =>
        new(View(room, userId), receipt) { Events = room.Projections[room.Players.IndexOf(userId)].ProjectEvents(events) };

    public DuelNamePage QueryNames(Guid userId, Guid roomId, string actionToken, string query, int offset)
    {
        var room = Find(roomId);
        lock (room.Gate)
        {
            RequireMember(room, userId);
            if (room.Status != "Running" || room.DisconnectedAt.Count != 0) throw Error(409, "DUEL_NOT_RUNNING", "对局当前不能查询宣言动作");
            if (offset < 0 || query.Length > 128) throw Error(400, "INVALID_NAME_QUERY", "名称检索范围无效");
            var seat = room.Players.IndexOf(userId);
            if (!room.Projections[seat].TryResolveNameDeclaration(room.Engine!.State, actionToken, out var command)
                || room.Engine.Abilities.Get(command.AbilityId) is not IActivationNameDeclaration declaration)
                throw Error(409, "STALE_OR_INVALID_ACTION", "当前席位的名称宣言动作已失效");
            var candidateIds = declaration.NameCandidates(new EffectContext(room.Engine, seat, command.CardId))
                .Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            var matches = names.Names.Where(name => candidateIds.Contains(name.NameId)
                && (query.Length == 0 || name.NameId.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || name.Chinese.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || name.Japanese.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || name.English.Contains(query, StringComparison.OrdinalIgnoreCase)))
                .OrderBy(name => name.NameId, StringComparer.Ordinal).ToArray();
            return new DuelNamePage(room.Engine.State.Revision, offset, matches.Length, matches.Skip(offset).Take(100).ToArray());
        }
    }

    static DuelStepResult ApplyAndRecord(Room room, DuelCommand command)
    {
        var result = room.Engine!.Apply(command);
        room.Replay!.Append(command, result, DuelStateDigest.Compute(room.Engine.State));
        return result;
    }

    // Server diagnostic interface only; this method has no player HTTP/WS route.
    public DuelReplayRecord CaptureReplay(Guid roomId)
    {
        var room = Find(roomId);
        lock (room.Gate)
        {
            RequireExisting(room);
            if (room.Replay == null) throw Error(409, "DUEL_NOT_STARTED", "房间尚未开始对局");
            return room.Replay.Capture();
        }
    }

    public IReadOnlyList<DuelReplayRecord> CaptureReplayHistory(Guid roomId)
    {
        var room = Find(roomId);
        lock (room.Gate)
        { RequireExisting(room); return room.ReplayHistory.ToArray(); }
    }

    void Complete(Room room)
    {
        room.Status = "Finished";
        room.FinishedAt = clock.GetUtcNow();
        room.ReplayHistory.Add(room.Replay!.Capture());
    }

    public void Tick()
    {
        Room[] snapshot;
        lock (gate) snapshot = rooms.Values.ToArray();
        foreach (var room in snapshot) lock (room.Gate) if (!room.Closed) AdvanceClock(room);
    }

    void AdvanceClock(Room room)
    {
        var now = clock.GetUtcNow();
        if (room.Status == "Waiting" && now - room.LastActivityAt >= TimeSpan.FromMinutes(30))
        { CloseRoom(room); return; }
        if (room.Status == "Finished" && now - room.FinishedAt >= TimeSpan.FromMinutes(10))
        { CloseRoom(room); return; }
        if (room.Status != "Running") return;
        if (room.DisconnectedAt.Count != 0)
        {
            room.ClockAt = now;
            var expired = room.DisconnectedAt.Where(pair => now - pair.Value >= TimeSpan.FromSeconds(60))
                .OrderBy(pair => pair.Value).ThenBy(pair => room.Players.IndexOf(pair.Key)).ToArray();
            if (expired.Length != 0)
            {
                var disconnected = room.Players.IndexOf(expired[0].Key);
                var result = ApplyAndRecord(room, new DuelCommand
                {
                    Kind = room.DisconnectedAt.Count == 2 ? DuelCommandKind.Abort : DuelCommandKind.DisconnectTimeout,
                    Player = disconnected
                });
                room.Sequence++;
                Complete(room);
                foreach (var player in room.Players) Send(room, player, Notice(room, player, result.Events, null));
            }
            return;
        }
        var waiting = room.Engine!.State.WaitingSeat;
        room.RemainingSeconds[waiting] = Math.Max(0, room.RemainingSeconds[waiting] - Math.Max(0, (now - room.ClockAt).TotalSeconds));
        room.ClockAt = now;
        if (room.RemainingSeconds[waiting] > 0) return;
        var step = ApplyAndRecord(room, new DuelCommand { Kind = DuelCommandKind.Timeout, Player = waiting });
        room.Sequence++;
        Complete(room);
        foreach (var player in room.Players) Send(room, player, Notice(room, player, step.Events, null));
    }

    void Send(Room room, Guid userId, DuelRoomNotice notice)
    {
        if (room.Connections.TryGetValue(userId, out var connection)
            && !connection.Channel.Writer.TryWrite(notice))
        {
            room.Connections.Remove(userId);
            connection.Stop();
            if (room.Status == "Running")
            {
                room.DisconnectedAt[userId] = clock.GetUtcNow();
                room.ClockAt = clock.GetUtcNow();
                room.Sequence++;
            }
            Publish(room);
        }
    }

    public void RejectMessage(Guid userId, Guid roomId, Guid connectionId, Guid requestId, string error)
    {
        var room = TryFind(roomId);
        if (room == null) return;
        lock (room.Gate)
        {
            if (room.Closed || !room.Connections.TryGetValue(userId, out var active)
                || active.Id != connectionId) return;
            Send(room, userId, new DuelRoomNotice(View(room, userId),
                new DuelCommandReceipt(requestId, false, error, room.Sequence, room.Engine?.State.Revision ?? 0)));
        }
    }

    public void Ping(Guid userId, Guid roomId, Guid connectionId)
    {
        var room = Find(roomId);
        lock (room.Gate)
        {
            RequireMember(room, userId);
            if (!room.Connections.TryGetValue(userId, out var connection) || connection.Id != connectionId)
                throw Error(409, "CONNECTION_REPLACED", "此连接已被新连接替换");
            AdvanceClock(room);
            Send(room, userId, new DuelRoomNotice(View(room, userId)));
        }
    }

    public void Disconnect(Guid userId, Guid roomId, Guid connectionId)
    {
        var room = TryFind(roomId);
        if (room == null) return;
        lock (room.Gate)
        {
            if (room.Closed || !room.Connections.TryGetValue(userId, out var connection)
                || connection.Id != connectionId) return;
            AdvanceClock(room);
            room.Connections.Remove(userId);
            connection.Stop();
            if (room.Status == "Running")
            {
                room.DisconnectedAt[userId] = clock.GetUtcNow();
                room.Sequence++;
                Publish(room);
            }
        }
    }

    void Publish(Room room)
    {
        foreach (var userId in room.Connections.Keys.ToArray())
            Send(room, userId, new DuelRoomNotice(View(room, userId)));
    }

    void RequireMember(Room room, Guid userId)
    {
        RequireExisting(room);
        if (!room.Players.Contains(userId) || room.Departed.Contains(userId))
            throw Error(403, "ROOM_FORBIDDEN", "账号不属于此房间");
        AdvanceClock(room);
        RequireExisting(room);
        if (room.Status == "Waiting") room.LastActivityAt = clock.GetUtcNow();
    }

    static void RequireExisting(Room room)
    { if (room.Closed) throw Error(404, "ROOM_NOT_FOUND", "房间不存在"); }

    Room? TryFind(Guid id) { lock (gate) return rooms.GetValueOrDefault(id); }
    Room Find(Guid id) => TryFind(id) ?? throw Error(404, "ROOM_NOT_FOUND", "房间不存在");

    void CloseRoom(Room room)
    {
        room.Closed = true;
        foreach (var connection in room.Connections.Values) connection.Stop();
        room.Connections.Clear();
        lock (gate)
        {
            codes.Remove(room.Code); rooms.Remove(room.Id);
            foreach (var player in room.Players)
                if (memberships.GetValueOrDefault(player) == room) memberships.Remove(player);
        }
    }

    static DuelRoomView View(Room room, Guid userId) => new(room.Id, room.Code, room.Status,
        room.Players.IndexOf(userId), room.Sequence,
        room.Players.Select((id, seat) => new DuelRoomPlayer(id, seat, room.Ready.Contains(id), room.Decks.ContainsKey(id))).ToArray(),
        room.Decks.TryGetValue(userId, out var deck) ? CopyDeck(deck) : null,
        room.Engine == null ? null : room.Projections[room.Players.IndexOf(userId)].Project(room.Engine.State,
            room.Engine.QueryLegalActions(room.Players.IndexOf(userId))), Array.AsReadOnly((double[])room.RemainingSeconds.Clone()),
        room.Status == "Running" && room.DisconnectedAt.Count != 0)
        { RulePackageHash = room.RulePackage.Fingerprint, RuleVersion = room.RulePackage.RuleVersion,
            ProtocolVersion = room.RulePackage.ProtocolVersion };

    static DuelDeckRequest CopyDeck(DuelDeckRequest deck) => new((string[])deck.MainDeck.Clone(), (string[])deck.ExtraDeck.Clone());

    DuelDeckRequest ValidateDeck(DuelDeckRequest deck)
    {
        if (deck.MainDeck.Length is < 40 or > 60 || deck.ExtraDeck.Length > 15)
            throw Error(422, "INVALID_DUEL_DECK", "主卡组需为40至60张，额外卡组至多15张");
        var main = Normalize(deck.MainDeck, false);
        var extra = Normalize(deck.ExtraDeck, true);
        foreach (var group in main.Concat(extra).GroupBy(id => catalog.Get(id).OriginalNameId, StringComparer.Ordinal))
            if (group.Count() > group.Min(id => catalog.Get(id).MaxCopies))
                throw Error(422, "INVALID_DUEL_DECK", "卡组违反当前同名或禁限数量限制");
        return new DuelDeckRequest(main, extra);
    }

    string[] Normalize(string[] ids, bool extra)
    {
        var result = new string[ids.Length];
        for (var i = 0; i < ids.Length; i++)
        {
            CardDefinition card;
            try { card = catalog.Get(ids[i]); }
            catch (KeyNotFoundException) { throw Error(422, "INVALID_DUEL_DECK", "构筑含当前卡池以外的卡牌"); }
            if (card.IsExtra != extra) throw Error(422, "INVALID_DUEL_DECK", "卡牌放入了错误的主或额外卡组区域");
            result[i] = card.CardId;
        }
        return result;
    }

    static string NewCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return new string(Enumerable.Range(0, 8).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }

    static ApiException Error(int status, string code, string message) => new(status, code, message);

    sealed class Room
    {
        public object Gate { get; } = new();
        public bool Closed { get; set; }
        public DuelRulePackage RulePackage { get; } = DuelRulePackage.CreateDefault(DuelCardCatalog.CreateDefault());
        public Guid Id { get; } = Guid.NewGuid();
        public required string Code { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset LastActivityAt { get; set; }
        public DateTimeOffset? FinishedAt { get; set; }
        public long Sequence { get; set; } = 1;
        public List<Guid> Players { get; } = new();
        public Dictionary<Guid, DuelDeckRequest> Decks { get; } = new();
        public HashSet<Guid> Ready { get; } = new();
        public HashSet<Guid> Departed { get; } = new();
        public string Status { get; set; } = "Waiting";
        public DuelEngine? Engine { get; set; }
        public DuelReplayRecorder? Replay { get; set; }
        public List<DuelReplayRecord> ReplayHistory { get; } = new();
        public SeatProjection[] Projections { get; } = { new(0), new(1) };
        public Dictionary<Guid, DuelRoomConnection> Connections { get; } = new();
        public Dictionary<(Guid UserId, Guid RequestId), (string Payload, DuelCommandReceipt Receipt)> Receipts { get; } = new();
        public DateTimeOffset ClockAt { get; set; }
        public double[] RemainingSeconds { get; } = { 180, 180 };
        public Dictionary<Guid, DateTimeOffset> DisconnectedAt { get; } = new();
    }
}
