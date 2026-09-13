using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddToolResources : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ToolResources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    VideoUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ImageStoragePath = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ImageContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ImageFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolResources", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ToolResourceTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ToolResourceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LanguageCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ToolResourceTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ToolResourceTranslations_ToolResources_ToolResourceId",
                        column: x => x.ToolResourceId,
                        principalTable: "ToolResources",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ToolResources_IsPublished_SortOrder",
                table: "ToolResources",
                columns: new[] { "IsPublished", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ToolResourceTranslations_ToolResourceId_LanguageCode",
                table: "ToolResourceTranslations",
                columns: new[] { "ToolResourceId", "LanguageCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ToolResourceTranslations");

            migrationBuilder.DropTable(
                name: "ToolResources");
        }
    }
}
