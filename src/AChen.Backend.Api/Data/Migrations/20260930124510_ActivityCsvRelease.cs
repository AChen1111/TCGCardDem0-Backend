using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActivityCsvRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityGiftRecord");

            migrationBuilder.DropColumn(
                name: "PublishStatus",
                table: "ActivityDefinitionRecord");

            // 不将旧 JSON 变成 CSV，也不把子表正文搬进新字段。
            foreach (var column in new[] { "DefinitionJson", "Action" })
                migrationBuilder.DropColumn(name: column, table: "ActivityPublishedVersion");
            foreach (var column in new[] { "Target", "DraftJson", "ConfigHash" })
                migrationBuilder.DropColumn(name: column, table: "ActivityDefinitionRecord");
            foreach (var column in new[] { "MasterJson", "DetailHash", "ReleaseId" })
                migrationBuilder.AddColumn<string>(name: column, table: "ActivityPublishedVersion", type: "TEXT", nullable: false, defaultValue: "");
            foreach (var column in new[] { "MasterJson", "DetailHash", "IdentityHash" })
                migrationBuilder.AddColumn<string>(name: column, table: "ActivityDefinitionRecord", type: "TEXT", nullable: false, defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ActivityReleaseRecord",
                columns: table => new
                {
                    ReleaseId = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    ManifestJson = table.Column<string>(type: "TEXT", nullable: false),
                    MasterJson = table.Column<string>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityReleaseRecord", x => x.ReleaseId);
                });

            migrationBuilder.CreateTable(
                name: "CurrentActivityRelease",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ReleaseId = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurrentActivityRelease", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityReleaseRecord");

            migrationBuilder.DropTable(
                name: "CurrentActivityRelease");

            foreach (var column in new[] { "MasterJson", "DetailHash", "ReleaseId" })
                migrationBuilder.DropColumn(name: column, table: "ActivityPublishedVersion");
            foreach (var column in new[] { "MasterJson", "DetailHash", "IdentityHash" })
                migrationBuilder.DropColumn(name: column, table: "ActivityDefinitionRecord");
            foreach (var column in new[] { "DefinitionJson", "Action" })
                migrationBuilder.AddColumn<string>(name: column, table: "ActivityPublishedVersion", type: "TEXT", nullable: false, defaultValue: "");
            foreach (var column in new[] { "Target", "DraftJson", "ConfigHash" })
                migrationBuilder.AddColumn<string>(name: column, table: "ActivityDefinitionRecord", type: "TEXT", nullable: false, defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "PublishStatus",
                table: "ActivityDefinitionRecord",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "ActivityGiftRecord",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    Frozen = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityGiftRecord", x => x.Id);
                });
        }
    }
}
