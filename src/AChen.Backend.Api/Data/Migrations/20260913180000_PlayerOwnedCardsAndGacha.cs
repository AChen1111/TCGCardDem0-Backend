using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260913180000_PlayerOwnedCardsAndGacha")]
public partial class PlayerOwnedCardsAndGacha : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "OwnedCards",
            table: "PlayerProfiles",
            type: "TEXT",
            nullable: false,
            defaultValue: "[]");

        migrationBuilder.CreateTable(
            name: "GachaPoolEntries",
            columns: table => new
            {
                PoolKey = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                CardId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                Weight = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GachaPoolEntries", x => new { x.PoolKey, x.CardId });
                table.CheckConstraint("CK_GachaPoolEntries_Weight_Positive", "Weight > 0");
            });

        migrationBuilder.CreateIndex(
            name: "IX_GachaPoolEntries_PoolKey",
            table: "GachaPoolEntries",
            column: "PoolKey");

        migrationBuilder.CreateTable(
            name: "GachaRarityWeights",
            columns: table => new
            {
                Rarity = table.Column<int>(type: "INTEGER", nullable: false),
                Weight = table.Column<int>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_GachaRarityWeights", x => x.Rarity);
                table.CheckConstraint("CK_GachaRarityWeights_Rarity_Range", "Rarity >= 0 AND Rarity <= 3");
                table.CheckConstraint("CK_GachaRarityWeights_Weight_Positive", "Weight > 0");
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "GachaPoolEntries");
        migrationBuilder.DropTable(name: "GachaRarityWeights");
        migrationBuilder.DropColumn(name: "OwnedCards", table: "PlayerProfiles");
    }
}
