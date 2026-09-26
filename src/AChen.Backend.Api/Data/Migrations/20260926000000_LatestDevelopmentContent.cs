using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace AChen.Backend.Api.Data.Migrations;
[DbContext(typeof(AppDbContext))]
[Migration("20260926000000_LatestDevelopmentContent")]
public sealed class LatestDevelopmentContent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ContentPublications");
        migrationBuilder.DropTable("ActiveContentReleases");
        migrationBuilder.DropTable("ContentReleaseFiles");
        migrationBuilder.DropTable("ContentReleases");
        migrationBuilder.CreateTable("CurrentContents", columns: table => new
        {
            Target = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
            ContentId = table.Column<string>(type: "TEXT", nullable: false),
            ManifestJson = table.Column<string>(type: "TEXT", nullable: false)
        }, constraints: table => table.PrimaryKey("PK_CurrentContents", x => x.Target));
    }
    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("旧发布历史已清理, 不支持回退; 业务数据不受影响.");
}
