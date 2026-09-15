using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace AChen.Backend.Api.Data.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20260915090000_PublishedConfiguration")]
public sealed class PublishedConfiguration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("AvatarDefinitions");
        migrationBuilder.DropTable("WallpaperDefinitions");
        migrationBuilder.DropTable("CardPackDefinitions");
        migrationBuilder.DropTable("GachaPoolEntries");
        migrationBuilder.DropTable("GachaRarityWeights");
        migrationBuilder.DropTable("AllCards");
        migrationBuilder.DropTable("GameConfigVersions");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("旧配置历史已删除。请恢复迁移前数据库备份，而非降级数据库结构。");
}
