using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260913210000_GachaRarityAllowGold")]
public partial class GachaRarityAllowGold : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        RebuildRarityTable(migrationBuilder, "Rarity >= 0 AND Rarity <= 4");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        RebuildRarityTable(migrationBuilder, "Rarity >= 0 AND Rarity <= 3");
    }

    // SQLite 不支持 DropCheckConstraint, 只能重建表改约束.
    static void RebuildRarityTable(MigrationBuilder migrationBuilder, string rarityCheck)
    {
        migrationBuilder.Sql("ALTER TABLE GachaRarityWeights RENAME TO GachaRarityWeights_old");
        migrationBuilder.Sql($"""
            CREATE TABLE GachaRarityWeights (
                Rarity INTEGER NOT NULL,
                Weight INTEGER NOT NULL,
                CONSTRAINT PK_GachaRarityWeights PRIMARY KEY (Rarity),
                CONSTRAINT CK_GachaRarityWeights_Rarity_Range CHECK ({rarityCheck}),
                CONSTRAINT CK_GachaRarityWeights_Weight_Positive CHECK (Weight > 0)
            )
            """);
        migrationBuilder.Sql(
            "INSERT INTO GachaRarityWeights (Rarity, Weight) SELECT Rarity, Weight FROM GachaRarityWeights_old");
        migrationBuilder.Sql("DROP TABLE GachaRarityWeights_old");
    }
}
