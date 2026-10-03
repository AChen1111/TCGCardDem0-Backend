using System.Text.Json;
using AChen.Backend.Api.Data;
using AChen.Backend.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.Duels;

public sealed class StoredDuelReplay
{
    public Guid Id { get; set; }
    public Guid Player0 { get; set; }
    public Guid Player1 { get; set; }
    public long StartedAtTicks { get; set; }
    public long FinishedAtTicks { get; set; }
    public int Winner { get; set; }
    public int TurnCount { get; set; }
    public string PlayersJson { get; set; } = "";
    public string TracksJson { get; set; } = "";
}

public sealed class DuelReplayStore(IServiceScopeFactory scopes)
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { IncludeFields = true };
    public void Save(StoredDuelReplay replay)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (db.DuelReplays.Any(x => x.Id == replay.Id)) return;
        db.DuelReplays.Add(replay);
        db.SaveChanges();
    }
    static DuelReplaySummary Summary(StoredDuelReplay replay, Guid userId) => new(replay.Id,
        new DateTimeOffset(replay.StartedAtTicks, TimeSpan.Zero), new DateTimeOffset(replay.FinishedAtTicks, TimeSpan.Zero),
        replay.Player0 == userId ? 0 : 1, replay.Winner, replay.TurnCount,
        JsonSerializer.Deserialize<DuelRoomPlayer[]>(replay.PlayersJson, Json)!);
    public IReadOnlyList<DuelReplaySummary> List(Guid userId)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return db.DuelReplays.AsNoTracking().Where(x => x.Player0 == userId || x.Player1 == userId)
            .OrderByDescending(x => x.FinishedAtTicks).ToArray().Select(x => Summary(x, userId)).ToArray();
    }
    public object Get(Guid userId, Guid id)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var replay = db.DuelReplays.AsNoTracking().SingleOrDefault(x => x.Id == id && (x.Player0 == userId || x.Player1 == userId))
            ?? throw new ApiException(404, "REPLAY_NOT_FOUND", "回放不存在");
        return new { summary = Summary(replay, userId), tracks = JsonSerializer.Deserialize<JsonElement>(replay.TracksJson) };
    }
}
