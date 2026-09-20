using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260920154500_GiftTitleKey")]
public sealed class GiftTitleKey : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "TitleKey",
            table: "Gifts",
            type: "TEXT",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TitleKey", table: "Gifts");
    }
}
