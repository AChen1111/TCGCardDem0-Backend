using System.Text.Json;
using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.Auth;
using AChen.Backend.Api.Features.Decks;
using AChen.Backend.Api.Features.Players;
using AChen.Backend.Api.Features.Social;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AChen.Backend.Api.Tests;

public sealed class RemoveThunderGiantMigrationTests
{
    [Fact]
    public async Task Migration_removes_card_from_inventory_art_decks_and_pending_gifts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20260928160000_CardArtIdentity");
        var now = DateTimeOffset.UtcNow;
        var user = new User { Username = "removedcard", NormalizedUsername = "REMOVEDCARD", PasswordHash = "hash", CreatedAt = now, UpdatedAt = now };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var profile = PlayerProfile.ForNewAccount(user.Id, "player", now);
        profile.OwnedCards = [new("19222426", 0, 2), new("01639384", 0, 1)];
        profile.OwnedArtIds = ["19222426", "01639384"];
        profile.Revision = 7;
        db.PlayerProfiles.Add(profile);
        db.PlayerDecks.Add(new PlayerDeck { UserId = user.Id, Name = "deck",
            MainDeckJson = "[{\"CardId\":\"19222426\",\"Rarity\":0,\"Count\":1}]",
            ExtraDeckJson = "[{\"CardId\":\"19222426\",\"Rarity\":0,\"Count\":1},{\"CardId\":\"01639384\",\"Rarity\":0,\"Count\":1}]",
            Revision = 3, CreatedAt = now, UpdatedAt = now });
        db.Gifts.Add(new Gift { TargetUserId = user.Id, Cards = [new("19222426", 0, 1), new("01639384", 0, 1)], CreatedAt = now });
        await db.SaveChangesAsync();

        await db.Database.MigrateAsync();
        db.ChangeTracker.Clear();

        var updated = await db.PlayerProfiles.SingleAsync();
        Assert.Equal(new OwnedCard("01639384", 0, 1), Assert.Single(updated.OwnedCards));
        Assert.Equal("01639384", Assert.Single(updated.OwnedArtIds));
        Assert.Equal(7, updated.Revision);
        var deck = await db.PlayerDecks.SingleAsync();
        Assert.Empty(JsonSerializer.Deserialize<DeckCardEntry[]>(deck.MainDeckJson)!);
        Assert.Equal("01639384", Assert.Single(JsonSerializer.Deserialize<DeckCardEntry[]>(deck.ExtraDeckJson)!).CardId);
        Assert.Equal(3, deck.Revision);
        Assert.Equal("01639384", Assert.Single((await db.Gifts.SingleAsync()).Cards).CardId);
    }
}
