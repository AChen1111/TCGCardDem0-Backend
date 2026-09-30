using System.Globalization;

namespace AChen.Backend.Api.Features.Activities;

public sealed class ActivityTimeProvider : TimeProvider
{
    readonly TimeProvider systemClock;
    readonly TimeSpan offset;

    public ActivityTimeProvider(TimeProvider systemClock, IHostEnvironment environment, IConfiguration configuration)
    {
        this.systemClock = systemClock;
        if (environment.IsDevelopment() && configuration["Activities:DevelopmentTime"] is { Length: > 0 } target)
            offset = DateTimeOffset.Parse(target, CultureInfo.InvariantCulture) - systemClock.GetUtcNow();
    }

    // 活动时间继续走动；登录令牌和账号时间仍使用原始 TimeProvider。
    public override DateTimeOffset GetUtcNow() => systemClock.GetUtcNow() + offset;
}
