using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.Auth;
using AChen.Backend.Api.Features.Players;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AChen.Backend.Api.Tests;

public sealed class LatestContentMigrationTests
{
    [Fact]
    public async Task Migration_preserves_account_and_assets_and_removes_legacy_content_tables()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260920154500_GiftTitleKey");
        var user = new User { Username = "migration", NormalizedUsername = "MIGRATION", PasswordHash = "preserved",
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow };
        db.Users.Add(user);
        db.PlayerProfiles.Add(new PlayerProfile { UserId = user.Id, Nickname = "preserve", Gold = 12345,
            OwnedAvatarIds = [0, 2] });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("INSERT INTO ContentReleases (Id,Platform,AppVersion,ContentVersion,State,FileCount,TotalBytes,CreatedAt,UpdatedAt) VALUES ('aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa','Android','0.1.0','1.0.0','Ready',0,0,0,0)");
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("preserved", (await db.Users.SingleAsync()).PasswordHash);
        var player = await db.PlayerProfiles.SingleAsync();
        Assert.Equal(12345, player.Gold);
        Assert.Equal(new[] { 0, 2 }, player.OwnedAvatarIds);
        Assert.Empty(await db.CurrentContents.ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table' AND name IN ('ContentReleases','ContentReleaseFiles','ContentPublications','ActiveContentReleases')";
        Assert.Equal(0L, (long)(await command.ExecuteScalarAsync())!);
    }
}
