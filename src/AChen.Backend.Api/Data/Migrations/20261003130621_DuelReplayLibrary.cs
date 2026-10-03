using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class DuelReplayLibrary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DuelReplays",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Player0 = table.Column<Guid>(type: "TEXT", nullable: false),
                    Player1 = table.Column<Guid>(type: "TEXT", nullable: false),
                    StartedAtTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    FinishedAtTicks = table.Column<long>(type: "INTEGER", nullable: false),
                    Winner = table.Column<int>(type: "INTEGER", nullable: false),
                    TurnCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PlayersJson = table.Column<string>(type: "TEXT", nullable: false),
                    TracksJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DuelReplays", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DuelReplays_Player0",
                table: "DuelReplays",
                column: "Player0");

            migrationBuilder.CreateIndex(
                name: "IX_DuelReplays_Player1",
                table: "DuelReplays",
                column: "Player1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DuelReplays");
        }
    }
}
