using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlayerBackgroundId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BackgroundId",
                table: "PlayerProfiles",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundId",
                table: "PlayerProfiles");
        }
    }
}
