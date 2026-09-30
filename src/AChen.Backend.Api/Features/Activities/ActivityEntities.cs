using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.Activities;

public sealed class ActivityGate { public SemaphoreSlim Mutex { get; } = new(1, 1); }
public sealed class ActivityDefinitionRecord
{
    public string Id { get; set; } = "";
    public string Target { get; set; } = "";
    public string ConfigHash { get; set; } = "";
    public string DraftJson { get; set; } = "";
    public long Revision { get; set; }
    public long ActiveVersion { get; set; }
    public int PublishStatus { get; set; }
}
public sealed class ActivityPublishedVersion
{
    public string ActivityId { get; set; } = "";
    public long Version { get; set; }
    public string DefinitionJson { get; set; } = "";
    public string Action { get; set; } = "publish";
    public string Actor { get; set; } = "";
    public string PublishedAt { get; set; } = "";
}
public sealed class ActivityGiftRecord
{
    public string Id { get; set; } = "";
    public string DefinitionJson { get; set; } = "";
    public long Revision { get; set; }
    public bool Frozen { get; set; }
}
public sealed class PlayerActivityProgress
{
    public Guid PlayerId { get; set; }
    public string ActivityId { get; set; } = "";
    public long Value { get; set; }
    public bool Completed { get; set; }
    public long Revision { get; set; }
}
public sealed class PlayerActivityVisit
{
    public Guid PlayerId { get; set; }
    public string ActivityId { get; set; } = "";
    public string ServerDay { get; set; } = "";
}
public sealed class PlayerActivityCounter
{
    public Guid PlayerId { get; set; }
    public string ActivityId { get; set; } = "";
    public string EntryId { get; set; } = "";
    public string PeriodKey { get; set; } = "";
    public int Count { get; set; }
}
public sealed class PlayerActivityClaim
{
    public Guid Id { get; set; }
    public Guid PlayerId { get; set; }
    public string ActivityId { get; set; } = "";
    public string EntryId { get; set; } = "";
    public string PeriodKey { get; set; } = "";
    public int Ordinal { get; set; }
    public long Version { get; set; }
    public string RewardSnapshot { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}
public sealed class PlayerActivityPopup
{
    public Guid PlayerId { get; set; }
    public string ActivityId { get; set; } = "";
    public long PolicyVersion { get; set; }
    public string PeriodKey { get; set; } = "";
    public string ShownAt { get; set; } = "";
}
public sealed class ActivityOperation
{
    public Guid PlayerId { get; set; }
    public string RequestId { get; set; } = "";
    public string PayloadHash { get; set; } = "";
    public string ResponseJson { get; set; } = "";
    public string CreatedAt { get; set; } = "";
}
public static class ActivityModel
{
    public static void Configure(ModelBuilder model)
    {
        model.Entity<ActivityDefinitionRecord>().HasKey(x => x.Id);
        model.Entity<ActivityDefinitionRecord>().Property(x => x.Revision).IsConcurrencyToken();
        model.Entity<ActivityGiftRecord>().HasKey(x => x.Id);
        model.Entity<ActivityGiftRecord>().Property(x => x.Revision).IsConcurrencyToken();
        model.Entity<ActivityPublishedVersion>().HasKey(x => new { x.ActivityId, x.Version });
        model.Entity<PlayerActivityProgress>().HasKey(x => new { x.PlayerId, x.ActivityId });
        model.Entity<PlayerActivityProgress>().Property(x => x.Revision).IsConcurrencyToken();
        model.Entity<PlayerActivityVisit>().HasKey(x => new { x.PlayerId, x.ActivityId, x.ServerDay });
        model.Entity<PlayerActivityCounter>().HasKey(x => new { x.PlayerId, x.ActivityId, x.EntryId, x.PeriodKey });
        model.Entity<PlayerActivityClaim>().HasKey(x => x.Id);
        model.Entity<PlayerActivityClaim>().HasIndex(x => new { x.PlayerId, x.ActivityId, x.EntryId, x.PeriodKey, x.Ordinal }).IsUnique();
        model.Entity<PlayerActivityPopup>().HasKey(x => new { x.PlayerId, x.ActivityId, x.PolicyVersion, x.PeriodKey });
        model.Entity<ActivityOperation>().HasKey(x => new { x.PlayerId, x.RequestId });
        model.Entity<ActivityPublishedVersion>().HasOne<ActivityDefinitionRecord>().WithMany().HasForeignKey(x => x.ActivityId).OnDelete(DeleteBehavior.Restrict);
        foreach (var entity in new[] { typeof(PlayerActivityProgress), typeof(PlayerActivityVisit), typeof(PlayerActivityCounter), typeof(PlayerActivityClaim), typeof(PlayerActivityPopup) })
            model.Entity(entity).HasOne(typeof(ActivityDefinitionRecord)).WithMany().HasForeignKey("ActivityId").OnDelete(DeleteBehavior.Restrict);
    }
}
