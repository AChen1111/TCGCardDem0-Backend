using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260920150000_SocialFriendsAndGifts")]
public sealed class SocialFriendsAndGifts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Friendships",
            columns: table => new
            {
                UserIdA = table.Column<Guid>(type: "TEXT", nullable: false),
                UserIdB = table.Column<Guid>(type: "TEXT", nullable: false),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Friendships", x => new { x.UserIdA, x.UserIdB });
            });

        migrationBuilder.CreateTable(
            name: "FriendRequests",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                FromUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                ToUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                Status = table.Column<int>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FriendRequests", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "Gifts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                TargetUserId = table.Column<Guid>(type: "TEXT", nullable: false),
                SourceUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                Gold = table.Column<long>(type: "INTEGER", nullable: false),
                Cards = table.Column<string>(type: "TEXT", nullable: false),
                Claimed = table.Column<bool>(type: "INTEGER", nullable: false),
                CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                ClaimedAt = table.Column<long>(type: "INTEGER", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Gifts", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FriendRequests_ToUserId_Status",
            table: "FriendRequests",
            columns: new[] { "ToUserId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_FriendRequests_FromUserId_ToUserId",
            table: "FriendRequests",
            columns: new[] { "FromUserId", "ToUserId" });

        migrationBuilder.CreateIndex(
            name: "IX_Gifts_TargetUserId_Claimed",
            table: "Gifts",
            columns: new[] { "TargetUserId", "Claimed" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "Friendships");
        migrationBuilder.DropTable(name: "FriendRequests");
        migrationBuilder.DropTable(name: "Gifts");
    }
}
