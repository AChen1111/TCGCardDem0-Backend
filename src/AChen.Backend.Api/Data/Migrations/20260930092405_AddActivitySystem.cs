using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActivitySystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityDefinitionRecord",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Target = table.Column<string>(type: "TEXT", nullable: false),
                    ConfigHash = table.Column<string>(type: "TEXT", nullable: false),
                    DraftJson = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    ActiveVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    PublishStatus = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityDefinitionRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ActivityGiftRecord",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    Frozen = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityGiftRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ActivityOperation",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RequestId = table.Column<string>(type: "TEXT", nullable: false),
                    PayloadHash = table.Column<string>(type: "TEXT", nullable: false),
                    ResponseJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityOperation", x => new { x.PlayerId, x.RequestId });
                });

            migrationBuilder.CreateTable(
                name: "ActivityPublishedVersion",
                columns: table => new
                {
                    ActivityId = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    DefinitionJson = table.Column<string>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Actor = table.Column<string>(type: "TEXT", nullable: false),
                    PublishedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityPublishedVersion", x => new { x.ActivityId, x.Version });
                    table.ForeignKey(
                        name: "FK_ActivityPublishedVersion_ActivityDefinitionRecord_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "ActivityDefinitionRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerActivityClaim",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivityId = table.Column<string>(type: "TEXT", nullable: false),
                    EntryId = table.Column<string>(type: "TEXT", nullable: false),
                    PeriodKey = table.Column<string>(type: "TEXT", nullable: false),
                    Ordinal = table.Column<int>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false),
                    RewardSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerActivityClaim", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlayerActivityClaim_ActivityDefinitionRecord_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "ActivityDefinitionRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerActivityCounter",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivityId = table.Column<string>(type: "TEXT", nullable: false),
                    EntryId = table.Column<string>(type: "TEXT", nullable: false),
                    PeriodKey = table.Column<string>(type: "TEXT", nullable: false),
                    Count = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerActivityCounter", x => new { x.PlayerId, x.ActivityId, x.EntryId, x.PeriodKey });
                    table.ForeignKey(
                        name: "FK_PlayerActivityCounter_ActivityDefinitionRecord_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "ActivityDefinitionRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerActivityPopup",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivityId = table.Column<string>(type: "TEXT", nullable: false),
                    PolicyVersion = table.Column<long>(type: "INTEGER", nullable: false),
                    PeriodKey = table.Column<string>(type: "TEXT", nullable: false),
                    ShownAt = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerActivityPopup", x => new { x.PlayerId, x.ActivityId, x.PolicyVersion, x.PeriodKey });
                    table.ForeignKey(
                        name: "FK_PlayerActivityPopup_ActivityDefinitionRecord_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "ActivityDefinitionRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerActivityProgress",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivityId = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    Completed = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerActivityProgress", x => new { x.PlayerId, x.ActivityId });
                    table.ForeignKey(
                        name: "FK_PlayerActivityProgress_ActivityDefinitionRecord_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "ActivityDefinitionRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlayerActivityVisit",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActivityId = table.Column<string>(type: "TEXT", nullable: false),
                    ServerDay = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlayerActivityVisit", x => new { x.PlayerId, x.ActivityId, x.ServerDay });
                    table.ForeignKey(
                        name: "FK_PlayerActivityVisit_ActivityDefinitionRecord_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "ActivityDefinitionRecord",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlayerActivityClaim_ActivityId",
                table: "PlayerActivityClaim",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerActivityClaim_PlayerId_ActivityId_EntryId_PeriodKey_Ordinal",
                table: "PlayerActivityClaim",
                columns: new[] { "PlayerId", "ActivityId", "EntryId", "PeriodKey", "Ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlayerActivityCounter_ActivityId",
                table: "PlayerActivityCounter",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerActivityPopup_ActivityId",
                table: "PlayerActivityPopup",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerActivityProgress_ActivityId",
                table: "PlayerActivityProgress",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_PlayerActivityVisit_ActivityId",
                table: "PlayerActivityVisit",
                column: "ActivityId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityGiftRecord");

            migrationBuilder.DropTable(
                name: "ActivityOperation");

            migrationBuilder.DropTable(
                name: "ActivityPublishedVersion");

            migrationBuilder.DropTable(
                name: "PlayerActivityClaim");

            migrationBuilder.DropTable(
                name: "PlayerActivityCounter");

            migrationBuilder.DropTable(
                name: "PlayerActivityPopup");

            migrationBuilder.DropTable(
                name: "PlayerActivityProgress");

            migrationBuilder.DropTable(
                name: "PlayerActivityVisit");

            migrationBuilder.DropTable(
                name: "ActivityDefinitionRecord");
        }
    }
}
