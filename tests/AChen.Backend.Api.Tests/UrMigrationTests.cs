using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.Auth;
using AChen.Backend.Api.Features.Decks;
using AChen.Backend.Api.Features.Players;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AChen.Backend.Api.Tests;

public sealed class UrMigrationTests
{
    [Fact]
    public async Task Ur_upgrade_preserves_account_inventory_decks_and_revision()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        await using var db=new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.GetService<IMigrator>().MigrateAsync("20260927122935_PlayerAvatarFrames");
        var user=new User { Username="UrMigration",NormalizedUsername="URMIGRATION",PasswordHash="unchanged",CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow };
        db.Users.Add(user);await db.SaveChangesAsync();
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PlayerProfiles (UserId,Nickname,AvatarId,OwnedAvatarIds,AvatarFrameId,OwnedAvatarFrameIds,BackgroundId,OwnedBackgroundIds,OwnedCards,Gold,Revision,CreatedAt,UpdatedAt) VALUES ({user.Id},'旧玩家',1010001,'[1010001]',1030001,'[1030001]',1,'[1]','[{{\"CardId\":\"01639384\",\"Rarity\":0,\"Count\":5}}]',1234,7,0,0)");
        var deck=new PlayerDeck { UserId=user.Id,Name="保留卡组",MainDeckJson="[]",ExtraDeckJson="[{\"CardId\":\"01639384\",\"Rarity\":0,\"Count\":2}]",Revision=3,CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow };
        db.PlayerDecks.Add(deck);await db.SaveChangesAsync();
        await db.Database.MigrateAsync();db.ChangeTracker.Clear();
        var player=await db.PlayerProfiles.SingleAsync();Assert.Equal(0,player.Ur);Assert.Equal(1234,player.Gold);Assert.Equal(7,player.Revision);
        Assert.Equal(new OwnedCard("01639384",0,5),Assert.Single(player.OwnedCards));Assert.Equal("旧玩家",player.Nickname);
        Assert.Equal("unchanged",(await db.Users.SingleAsync()).PasswordHash);
        var preserved=await db.PlayerDecks.SingleAsync();Assert.Equal(deck.ExtraDeckJson,preserved.ExtraDeckJson);Assert.Equal(3,preserved.Revision);
        player.Ur=75;await db.SaveChangesAsync();await db.Database.MigrateAsync();db.ChangeTracker.Clear();Assert.Equal(75,(await db.PlayerProfiles.SingleAsync()).Ur);
    }
}
