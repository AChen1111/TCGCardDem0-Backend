using AChen.Backend.Api.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AChen.Backend.Api.Tests;

public sealed class GameConfigMigrationTests
{
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
                await migrator.MigrateAsync();
            }

            await using (var db = new AppDbContext(options))
            {
                var profiles = await db.PlayerProfiles.OrderBy(value => value.Nickname).ToArrayAsync();
                Assert.Equal(2, profiles.Length);
                Assert.Null(profiles.Single(value => value.Nickname == "legacy").AvatarId);
                Assert.Equal(42, profiles.Single(value => value.Nickname == "numeric").AvatarId);
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
