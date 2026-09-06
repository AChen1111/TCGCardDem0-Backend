using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260906150000_CatalogItemAvailability")]
public partial class CatalogItemAvailability : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(
            name: "StartsAt",
            table: "AvatarDefinitions",
            type: "INTEGER",
            nullable: true);
        migrationBuilder.AddColumn<long>(
            name: "EndsAt",
            table: "AvatarDefinitions",
            type: "INTEGER",
            nullable: true);
        migrationBuilder.AddColumn<long>(
            name: "StartsAt",
            table: "WallpaperDefinitions",
            type: "INTEGER",
            nullable: true);
        migrationBuilder.AddColumn<long>(
            name: "EndsAt",
            table: "WallpaperDefinitions",
            type: "INTEGER",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "StartsAt", table: "AvatarDefinitions");
        migrationBuilder.DropColumn(name: "EndsAt", table: "AvatarDefinitions");
        migrationBuilder.DropColumn(name: "StartsAt", table: "WallpaperDefinitions");
        migrationBuilder.DropColumn(name: "EndsAt", table: "WallpaperDefinitions");
    }
}
