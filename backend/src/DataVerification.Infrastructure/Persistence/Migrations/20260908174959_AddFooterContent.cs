using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddFooterContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FooterLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Column = table.Column<int>(type: "int", nullable: false),
                    Visibility = table.Column<int>(type: "int", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FooterLinks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FooterLogos",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ImageStoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ImageContentType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ImageFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    Url = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FooterLogos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FooterLinkTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FooterLinkId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LanguageCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FooterLinkTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FooterLinkTranslations_FooterLinks_FooterLinkId",
                        column: x => x.FooterLinkId,
                        principalTable: "FooterLinks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FooterLogoTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FooterLogoId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LanguageCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Alt = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FooterLogoTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FooterLogoTranslations_FooterLogos_FooterLogoId",
                        column: x => x.FooterLogoId,
                        principalTable: "FooterLogos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FooterLinks_Column_IsActive_SortOrder",
                table: "FooterLinks",
                columns: new[] { "Column", "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_FooterLinkTranslations_FooterLinkId_LanguageCode",
                table: "FooterLinkTranslations",
                columns: new[] { "FooterLinkId", "LanguageCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FooterLogos_IsActive_SortOrder",
                table: "FooterLogos",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_FooterLogoTranslations_FooterLogoId_LanguageCode",
                table: "FooterLogoTranslations",
                columns: new[] { "FooterLogoId", "LanguageCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FooterLinkTranslations");

            migrationBuilder.DropTable(
                name: "FooterLogoTranslations");

            migrationBuilder.DropTable(
                name: "FooterLinks");

            migrationBuilder.DropTable(
                name: "FooterLogos");
        }
    }
}
