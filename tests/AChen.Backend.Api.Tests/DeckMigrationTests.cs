using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.Auth;
using AChen.Backend.Api.Features.Players;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AChen.Backend.Api.Tests;

public sealed class DeckMigrationTests
{
    [Fact]
    public async Task Adding_decks_preserves_existing_account_and_inventory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260926000000_LatestDevelopmentContent");
        var now = DateTimeOffset.UtcNow;
        var user = new User { Username = "DeckMigration", NormalizedUsername = "DECKMIGRATION", PasswordHash = "preserved",
            CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        // 使用升级前的真实表结构，避免当前实体新增字段影响历史迁移测试。
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PlayerProfiles (UserId,Nickname,AvatarId,OwnedAvatarIds,BackgroundId,OwnedBackgroundIds,OwnedCards,Gold,Revision,CreatedAt,UpdatedAt) VALUES ({user.Id},'DeckMigration',0,'[0]',1,'[1]','[{{\"CardId\":\"01639384\",\"Rarity\":1,\"Count\":3}}]',12345,7,0,0)");
        await migrator.MigrateAsync("20260926183745_PlayerDecks");
        db.ChangeTracker.Clear();
        Assert.Equal("preserved", (await db.Users.SingleAsync()).PasswordHash);
        var saved = await db.PlayerProfiles.Select(value => new { value.Gold, value.Revision, value.OwnedCards }).SingleAsync();
        Assert.Equal(12345, saved.Gold);
        Assert.Equal(7, saved.Revision);
        Assert.Equal(new OwnedCard("01639384", 1, 3), Assert.Single(saved.OwnedCards));
        Assert.Empty(await db.PlayerDecks.ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
