using AChen.Backend.Api.Features.Activities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace AChen.Backend.Api.Tests;

public sealed class ActivityTimeProviderTests
{
    [Fact]
    public void Development_clock_starts_at_target_and_keeps_advancing_without_changing_system_clock()
    {
        var system = new MutableClock();
        var clock = Create(system, Environments.Development, "2026-10-01T12:00:00+08:00");
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T12:00:00+08:00"), clock.GetUtcNow());
        Assert.Equal(DateTimeOffset.Parse("2026-09-30T23:30:00+08:00"), system.GetUtcNow());
        system.Now = system.Now.AddMinutes(10);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T12:10:00+08:00"), clock.GetUtcNow());
    }

    [Fact]
    public void Development_without_override_uses_real_time()
    {
        var system = new MutableClock();
        Assert.Equal(system.Now, Create(system, Environments.Development, "").GetUtcNow());
    }

    [Theory]
    [InlineData("Testing")]
    [InlineData("Production")]
    public void Non_development_environment_ignores_override(string environment)
    {
        var system = new MutableClock();
        Assert.Equal(system.Now, Create(system, environment, "2026-10-01T12:00:00+08:00").GetUtcNow());
    }

    static ActivityTimeProvider Create(TimeProvider system, string environment, string target) => new(system,
        new TestEnvironment { EnvironmentName = environment }, new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Activities:DevelopmentTime"] = target }).Build());

    sealed class MutableClock : TimeProvider
    {
        public DateTimeOffset Now = DateTimeOffset.Parse("2026-09-30T23:30:00+08:00");
        public override DateTimeOffset GetUtcNow() => Now;
    }

    sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "";
        public string ApplicationName { get; set; } = "";
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
