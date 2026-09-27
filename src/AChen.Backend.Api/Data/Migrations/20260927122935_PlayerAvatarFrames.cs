using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class PlayerAvatarFrames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AvatarFrameId",
                table: "PlayerProfiles",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1030001);

            migrationBuilder.AddColumn<string>(
                name: "OwnedAvatarFrameIds",
                table: "PlayerProfiles",
                type: "TEXT",
                nullable: false,
                defaultValue: "[1030001]");

            migrationBuilder.Sql("UPDATE PlayerProfiles SET AvatarId = 1010001, OwnedAvatarIds = '[1010001]', AvatarFrameId = 1030001, OwnedAvatarFrameIds = '[1030001]', Revision = Revision + 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AvatarFrameId",
                table: "PlayerProfiles");

            migrationBuilder.DropColumn(
                name: "OwnedAvatarFrameIds",
                table: "PlayerProfiles");
        }
    }
}
