using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260913190000_AllCards")]
public partial class AllCards : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AllCards",
            columns: table => new
            {
                CardId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                SourcePool = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AllCards", x => x.CardId);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AllCards");
    }
}
