using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.Auth;
using AChen.Backend.Api.Features.Decks;
using AChen.Backend.Api.Features.Players;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AChen.Backend.Api.Tests;

public sealed class GameConfigMigrationTests
{
    [Fact]
    public async Task Alternate_art_migration_merges_rule_cards_and_preserves_unlocked_art()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20260927122935_PlayerAvatarFrames");
        var now = DateTimeOffset.UtcNow;
        var user = new User { Username = "art", NormalizedUsername = "ART", PasswordHash = "hash", CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PlayerProfiles (UserId,Nickname,AvatarId,OwnedAvatarIds,AvatarFrameId,OwnedAvatarFrameIds,BackgroundId,OwnedBackgroundIds,OwnedCards,Gold,Revision,CreatedAt,UpdatedAt) VALUES ({user.Id},'art',1010001,'[1010001]',1030001,'[1030001]',1,'[1]','[{{\"CardId\":\"14558127\",\"Rarity\":0,\"Count\":2}},{{\"CardId\":\"14558128\",\"Rarity\":0,\"Count\":3}}]',0,7,0,0)");
        db.PlayerDecks.Add(new PlayerDeck { UserId = user.Id, Name = "异画卡组",
            MainDeckJson = "[{\"CardId\":\"14558127\",\"Rarity\":0,\"Count\":1},{\"CardId\":\"14558128\",\"Rarity\":0,\"Count\":2}]",
            ExtraDeckJson = "[]", Revision = 3, CreatedAt = now, UpdatedAt = now });
        await db.SaveChangesAsync();
        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();
        var profile = await db.PlayerProfiles.SingleAsync();
        Assert.Equal(new OwnedCard("14558127", 0, 5), Assert.Single(profile.OwnedCards));
        Assert.Equal(new[] { "14558127", "14558128" }, profile.OwnedArtIds.OrderBy(x => x));
        Assert.Equal(7, profile.Revision);
        var deck = await db.PlayerDecks.SingleAsync();
        Assert.Equal(new DeckCardEntry("14558127", 0, 3), Assert.Single(System.Text.Json.JsonSerializer.Deserialize<DeckCardEntry[]>(deck.MainDeckJson)!));
        Assert.Equal(3, deck.Revision);
    }
    [Fact]
    public async Task Migration_preserves_positive_numeric_avatar_ids_and_clears_legacy_keys()
    {
        string databasePath = Path.Combine(Path.GetTempPath(), $"achen-migration-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        try
        {
            await using (var db = new AppDbContext(options))
            {
                var migrator = db.Database.GetService<IMigrator>();
                await migrator.MigrateAsync("20260823094359_PlayerProfiles");
                var numericUser = Guid.NewGuid();
                var legacyUser = Guid.NewGuid();
                await db.Database.ExecuteSqlRawAsync(UserInsertSql(numericUser, "numeric"));
                await db.Database.ExecuteSqlRawAsync(UserInsertSql(legacyUser, "legacy"));
                await db.Database.ExecuteSqlRawAsync(ProfileInsertSql(numericUser, "42"));
                await db.Database.ExecuteSqlRawAsync(ProfileInsertSql(legacyUser, "avatar.default"));
                await migrator.MigrateAsync("20260902150000_PlayerOwnedAvatarIds");
                await db.Database.ExecuteSqlRawAsync("UPDATE PlayerProfiles SET BackgroundId = 1");
                await migrator.MigrateAsync("20260926000000_LatestDevelopmentContent");
            }

            await using (var db = new AppDbContext(options))
            {
                var profiles = await db.PlayerProfiles.OrderBy(value => value.Nickname)
                    .Select(value => new { value.Nickname, value.AvatarId, value.OwnedBackgroundIds }).ToArrayAsync();
                Assert.Equal(2, profiles.Length);
                Assert.Null(profiles.Single(value => value.Nickname == "legacy").AvatarId);
                Assert.Equal(42, profiles.Single(value => value.Nickname == "numeric").AvatarId);
                Assert.All(profiles, profile => Assert.Equal(new[] { 1 }, profile.OwnedBackgroundIds));
                await db.Database.OpenConnectionAsync();
                using var command = db.Database.GetDbConnection().CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name IN ('GameConfigVersions', 'AvatarDefinitions', 'WallpaperDefinitions', 'CardPackDefinitions', 'AllCards', 'GachaPoolEntries', 'GachaRarityWeights')";
                Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
            }
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    private static string UserInsertSql(Guid id, string name) =>
        $"""
        INSERT INTO Users
            (Id, Username, NormalizedUsername, Email, NormalizedEmail, PasswordHash, CreatedAt, UpdatedAt)
        VALUES
            ('{id:D}', '{name}', '{name.ToUpperInvariant()}', '{name}@example.com', '{name.ToUpperInvariant()}@EXAMPLE.COM', 'hash', '2026-08-23T00:00:00+00:00', '2026-08-23T00:00:00+00:00')
        """;

    private static string ProfileInsertSql(Guid id, string avatarId) =>
        $"""
        INSERT INTO PlayerProfiles
            (UserId, Nickname, AvatarId, Gold, Revision, CreatedAt, UpdatedAt)
        VALUES
            ('{id:D}', '{(avatarId == "42" ? "numeric" : "legacy")}', '{avatarId}', 0, 0, 0, 0)
        """;
}
