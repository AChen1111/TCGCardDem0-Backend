using AChen.Backend.Api.Data;
using AChen.Backend.Api.Features.Auth;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AChen.Backend.Api.Tests;

public sealed class AvatarMigrationTests
{
    [Fact]
    public async Task Legacy_inventory_resets_once_and_preserves_other_assets()
    {
        await using var connection=new SqliteConnection("Data Source=:memory:");await connection.OpenAsync();
        var options=new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
        await using(var db=new AppDbContext(options))
        {
            await db.GetService<IMigrator>().MigrateAsync("20260926183745_PlayerDecks");
            var user=new User{Username="Legacy",NormalizedUsername="LEGACY",PasswordHash="test",CreatedAt=DateTimeOffset.UtcNow,UpdatedAt=DateTimeOffset.UtcNow};
            db.Users.Add(user);await db.SaveChangesAsync();
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO PlayerProfiles (UserId,Nickname,AvatarId,OwnedAvatarIds,BackgroundId,OwnedBackgroundIds,OwnedCards,Gold,Revision,CreatedAt,UpdatedAt) VALUES ({user.Id},'原昵称',4,'[0,4,8]',1,'[1,3]','[]',1234,7,0,0)");
            await db.Database.MigrateAsync();
            var player=await db.PlayerProfiles.SingleAsync();
            Assert.Equal(1010001,player.AvatarId);Assert.Equal(new[]{1010001},player.OwnedAvatarIds);
            Assert.Equal(1030001,player.AvatarFrameId);Assert.Equal(new[]{1030001},player.OwnedAvatarFrameIds);
            Assert.Equal(1234,player.Gold);Assert.Equal("原昵称",player.Nickname);Assert.Equal(new[]{1,3},player.OwnedBackgroundIds);Assert.Equal(8,player.Revision);
            player.OwnedAvatarIds.Add(1010002);player.OwnedAvatarFrameIds.Add(1030002);player.AvatarId=1010002;player.AvatarFrameId=1030002;
            await db.SaveChangesAsync();
        }
        await using(var db=new AppDbContext(options))
        {
            await db.Database.MigrateAsync();var player=await db.PlayerProfiles.SingleAsync();
            Assert.Equal(1010002,player.AvatarId);Assert.Equal(1030002,player.AvatarFrameId);
            Assert.Equal(new[]{1010001,1010002},player.OwnedAvatarIds);Assert.Equal(new[]{1030001,1030002},player.OwnedAvatarFrameIds);
        }
    }
}
