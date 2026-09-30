using Microsoft.EntityFrameworkCore;

namespace AChen.Backend.Api.Features.Activities;

public sealed class ActivityGate { public SemaphoreSlim Mutex { get; } = new(1, 1); }
// 这里只保存总表和文件签名；奖励档位正文留在不可变版本文件中。
public sealed class ActivityDefinitionRecord
{
    public string Id { get; set; } = "";
    public string MasterJson { get; set; } = "";
    public string DetailHash { get; set; } = "";
    public string IdentityHash { get; set; } = "";
    public long Revision { get; set; }
    public long ActiveVersion { get; set; }
}
public sealed class ActivityPublishedVersion
{
    public string ActivityId { get; set; } = "";
    public long Version { get; set; }
    public string MasterJson { get; set; } = "";
    public string DetailHash { get; set; } = "";
    public string ReleaseId { get; set; } = "";
    public string Actor { get; set; } = "";
    public string PublishedAt { get; set; } = "";
}
public sealed class CurrentActivityRelease
{
    public int Id { get; set; } = 1;
    public string ReleaseId { get; set; } = "";
    public long Revision { get; set; }
}
public sealed class ActivityReleaseRecord
{
    public string ReleaseId { get; set; } = "";
    public long Revision { get; set; }
    public string ManifestJson { get; set; } = "";
    public string MasterJson { get; set; } = "";
    public string Actor { get; set; } = "";
    public string PublishedAt { get; set; } = "";
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
        model.Entity<CurrentActivityRelease>().HasKey(x => x.Id);
        model.Entity<CurrentActivityRelease>().Property(x => x.Revision).IsConcurrencyToken();
        model.Entity<ActivityReleaseRecord>().HasKey(x => x.ReleaseId);
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
