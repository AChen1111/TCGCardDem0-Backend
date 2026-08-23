using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AChen.Backend.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ContentDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ContentReleases",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AppVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ContentVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 24, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    FileCount = table.Column<int>(type: "INTEGER", nullable: false),
                    TotalBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    ArtifactSha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    HotUpdatePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CatalogPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CatalogHashPath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ReadyAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentReleases", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ActiveContentReleases",
                columns: table => new
                {
                    Channel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AppVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActiveContentReleases", x => new { x.Channel, x.Platform, x.AppVersion });
                    table.ForeignKey(
                        name: "FK_ActiveContentReleases_ContentReleases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "ContentReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContentPublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Channel = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Platform = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AppVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    PreviousReleaseId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentPublications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContentPublications_ContentReleases_PreviousReleaseId",
                        column: x => x.PreviousReleaseId,
                        principalTable: "ContentReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ContentPublications_ContentReleases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "ContentReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContentReleaseFiles",
                columns: table => new
                {
                    ReleaseId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RelativePath = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentReleaseFiles", x => new { x.ReleaseId, x.RelativePath });
                    table.ForeignKey(
                        name: "FK_ContentReleaseFiles_ContentReleases_ReleaseId",
                        column: x => x.ReleaseId,
                        principalTable: "ContentReleases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActiveContentReleases_ReleaseId",
                table: "ActiveContentReleases",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPublications_Channel_Platform_AppVersion_CreatedAt",
                table: "ContentPublications",
                columns: new[] { "Channel", "Platform", "AppVersion", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentPublications_PreviousReleaseId",
                table: "ContentPublications",
                column: "PreviousReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentPublications_ReleaseId",
                table: "ContentPublications",
                column: "ReleaseId");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReleases_CreatedAt",
                table: "ContentReleases",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReleases_Platform_AppVersion_ContentVersion",
                table: "ContentReleases",
                columns: new[] { "Platform", "AppVersion", "ContentVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActiveContentReleases");

            migrationBuilder.DropTable(
                name: "ContentPublications");

            migrationBuilder.DropTable(
                name: "ContentReleaseFiles");

            migrationBuilder.DropTable(
                name: "ContentReleases");
        }
    }
}
