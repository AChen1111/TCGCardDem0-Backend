using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260906120000_GameCatalogV2")]
public partial class GameCatalogV2 : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "OwnedBackgroundIds",
            table: "PlayerProfiles",
            type: "TEXT",
            nullable: false,
            defaultValue: "[]");
        migrationBuilder.Sql(
            "UPDATE PlayerProfiles SET OwnedBackgroundIds = CASE WHEN BackgroundId IS NULL THEN '[]' ELSE '[' || BackgroundId || ']' END");

        migrationBuilder.AddColumn<long>(
            name: "PriceGold",
            table: "AvatarDefinitions",
            type: "INTEGER",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.Sql("ALTER TABLE AvatarDefinitions RENAME TO AvatarDefinitions_old");
        migrationBuilder.Sql("""
            CREATE TABLE AvatarDefinitions (
                Revision INTEGER NOT NULL,
                Id INTEGER NOT NULL,
                Name TEXT NOT NULL,
                ResourceKey TEXT NOT NULL,
                SortOrder INTEGER NOT NULL,
                IsEnabled INTEGER NOT NULL,
                PriceGold INTEGER NOT NULL,
                CONSTRAINT PK_AvatarDefinitions PRIMARY KEY (Revision, Id),
                CONSTRAINT CK_AvatarDefinitions_Id_NonNegative CHECK (Id >= 0),
                CONSTRAINT CK_AvatarDefinitions_PriceGold_NonNegative CHECK (PriceGold >= 0),
                CONSTRAINT FK_AvatarDefinitions_GameConfigVersions_Revision FOREIGN KEY (Revision) REFERENCES GameConfigVersions (Revision) ON DELETE CASCADE
            )
            """);
        migrationBuilder.Sql("INSERT INTO AvatarDefinitions (Revision, Id, Name, ResourceKey, SortOrder, IsEnabled, PriceGold) SELECT Revision, Id, Name, ResourceKey, SortOrder, IsEnabled, PriceGold FROM AvatarDefinitions_old");
        migrationBuilder.Sql("DROP TABLE AvatarDefinitions_old");
        migrationBuilder.CreateIndex(
            name: "IX_AvatarDefinitions_Revision_ResourceKey",
            table: "AvatarDefinitions",
            columns: new[] { "Revision", "ResourceKey" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_AvatarDefinitions_Revision_SortOrder_Id",
            table: "AvatarDefinitions",
            columns: new[] { "Revision", "SortOrder", "Id" });

        migrationBuilder.CreateTable(
            name: "WallpaperDefinitions",
            columns: table => new
            {
                Revision = table.Column<long>(type: "INTEGER", nullable: false),
                Id = table.Column<int>(type: "INTEGER", nullable: false),
                Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                ResourceKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                PriceGold = table.Column<long>(type: "INTEGER", nullable: false),
                SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_WallpaperDefinitions", x => new { x.Revision, x.Id });
                table.CheckConstraint("CK_WallpaperDefinitions_Id_NonNegative", "Id >= 0");
                table.CheckConstraint("CK_WallpaperDefinitions_PriceGold_NonNegative", "PriceGold >= 0");
                table.ForeignKey("FK_WallpaperDefinitions_GameConfigVersions_Revision", x => x.Revision, "GameConfigVersions", "Revision", onDelete: ReferentialAction.Cascade);
            });
        migrationBuilder.CreateIndex("IX_WallpaperDefinitions_Revision_ResourceKey", "WallpaperDefinitions", new[] { "Revision", "ResourceKey" }, unique: true);
        migrationBuilder.CreateIndex("IX_WallpaperDefinitions_Revision_SortOrder_Id", "WallpaperDefinitions", new[] { "Revision", "SortOrder", "Id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "WallpaperDefinitions");
        migrationBuilder.DropColumn(name: "OwnedBackgroundIds", table: "PlayerProfiles");
        migrationBuilder.Sql("ALTER TABLE AvatarDefinitions RENAME TO AvatarDefinitions_v2");
        migrationBuilder.Sql("""
            CREATE TABLE AvatarDefinitions (
                Revision INTEGER NOT NULL,
                Id INTEGER NOT NULL,
                Name TEXT NOT NULL,
                ResourceKey TEXT NOT NULL,
                SortOrder INTEGER NOT NULL,
                IsEnabled INTEGER NOT NULL,
                CONSTRAINT PK_AvatarDefinitions PRIMARY KEY (Revision, Id),
                CONSTRAINT CK_AvatarDefinitions_Id_Positive CHECK (Id > 0),
                CONSTRAINT FK_AvatarDefinitions_GameConfigVersions_Revision FOREIGN KEY (Revision) REFERENCES GameConfigVersions (Revision) ON DELETE CASCADE
            )
            """);
        migrationBuilder.Sql("INSERT INTO AvatarDefinitions (Revision, Id, Name, ResourceKey, SortOrder, IsEnabled) SELECT Revision, Id, Name, ResourceKey, SortOrder, IsEnabled FROM AvatarDefinitions_v2 WHERE Id > 0");
        migrationBuilder.Sql("DROP TABLE AvatarDefinitions_v2");
        migrationBuilder.CreateIndex(
            name: "IX_AvatarDefinitions_Revision_ResourceKey",
            table: "AvatarDefinitions",
            columns: new[] { "Revision", "ResourceKey" },
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_AvatarDefinitions_Revision_SortOrder_Id",
            table: "AvatarDefinitions",
            columns: new[] { "Revision", "SortOrder", "Id" });
    }
}
