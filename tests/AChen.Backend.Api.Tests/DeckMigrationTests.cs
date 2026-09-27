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
        var player = PlayerProfile.ForNewAccount(user.Id, user.Username, now);
        player.OwnedCards = [new OwnedCard("01639384", 1, 3)];
        player.Gold = 12345;
        player.Revision = 7;
        db.Users.Add(user);
        db.PlayerProfiles.Add(player);
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        db.ChangeTracker.Clear();
        Assert.Equal("preserved", (await db.Users.SingleAsync()).PasswordHash);
        var saved = await db.PlayerProfiles.SingleAsync();
        Assert.Equal(12345, saved.Gold);
        Assert.Equal(7, saved.Revision);
        Assert.Equal(new OwnedCard("01639384", 1, 3), Assert.Single(saved.OwnedCards));
        Assert.Empty(await db.PlayerDecks.ToListAsync());
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
