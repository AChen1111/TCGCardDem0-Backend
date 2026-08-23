using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class GameConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "AvatarId",
                table: "PlayerProfiles",
                newName: "AvatarIdLegacy");

            migrationBuilder.AddColumn<int>(
                name: "AvatarId",
                table: "PlayerProfiles",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE PlayerProfiles
                SET AvatarId = CASE
                    WHEN AvatarIdLegacy IS NOT NULL
                         AND trim(AvatarIdLegacy) <> ''
                         AND trim(AvatarIdLegacy) NOT GLOB '*[^0-9]*'
                         AND CAST(trim(AvatarIdLegacy) AS INTEGER) BETWEEN 1 AND 2147483647
                    THEN CAST(trim(AvatarIdLegacy) AS INTEGER)
                    ELSE NULL
                END
                """);

            migrationBuilder.DropColumn(
                name: "AvatarIdLegacy",
                table: "PlayerProfiles");

            migrationBuilder.CreateTable(
                name: "GameConfigVersions",
                columns: table => new
                {
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 16, nullable: false),
                    EditRevision = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GameConfigVersions", x => x.Revision);
                    table.CheckConstraint("CK_GameConfigVersions_EditRevision_NonNegative", "EditRevision >= 0");
                });

            migrationBuilder.CreateTable(
                name: "AvatarDefinitions",
                columns: table => new
                {
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ResourceKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AvatarDefinitions", x => new { x.Revision, x.Id });
                    table.CheckConstraint("CK_AvatarDefinitions_Id_Positive", "Id > 0");
                    table.ForeignKey(
                        name: "FK_AvatarDefinitions_GameConfigVersions_Revision",
                        column: x => x.Revision,
                        principalTable: "GameConfigVersions",
                        principalColumn: "Revision",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CardPackDefinitions",
                columns: table => new
                {
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CoverResourceKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    PriceGold = table.Column<long>(type: "INTEGER", nullable: false),
                    StartsAt = table.Column<long>(type: "INTEGER", nullable: true),
                    EndsAt = table.Column<long>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CardPackDefinitions", x => new { x.Revision, x.Id });
                    table.CheckConstraint("CK_CardPackDefinitions_Id_Positive", "Id > 0");
                    table.CheckConstraint("CK_CardPackDefinitions_PriceGold_NonNegative", "PriceGold >= 0");
                    table.ForeignKey(
                        name: "FK_CardPackDefinitions_GameConfigVersions_Revision",
                        column: x => x.Revision,
                        principalTable: "GameConfigVersions",
                        principalColumn: "Revision",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AvatarDefinitions_Revision_ResourceKey",
                table: "AvatarDefinitions",
                columns: new[] { "Revision", "ResourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AvatarDefinitions_Revision_SortOrder_Id",
                table: "AvatarDefinitions",
                columns: new[] { "Revision", "SortOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_CardPackDefinitions_Revision_SortOrder_Id",
                table: "CardPackDefinitions",
                columns: new[] { "Revision", "SortOrder", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_GameConfigVersions_State",
                table: "GameConfigVersions",
                column: "State",
                unique: true,
                filter: "\"State\" = 'Draft'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AvatarDefinitions");

            migrationBuilder.DropTable(
                name: "CardPackDefinitions");

            migrationBuilder.DropTable(
                name: "GameConfigVersions");

            migrationBuilder.RenameColumn(
                name: "AvatarId",
                table: "PlayerProfiles",
                newName: "AvatarIdInteger");

            migrationBuilder.AddColumn<string>(
                name: "AvatarId",
                table: "PlayerProfiles",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.Sql(
                "UPDATE PlayerProfiles SET AvatarId = CAST(AvatarIdInteger AS TEXT) WHERE AvatarIdInteger IS NOT NULL");

            migrationBuilder.DropColumn(
                name: "AvatarIdInteger",
                table: "PlayerProfiles");
        }
    }
}
