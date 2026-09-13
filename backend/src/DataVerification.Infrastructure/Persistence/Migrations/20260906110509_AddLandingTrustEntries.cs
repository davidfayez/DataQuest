using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLandingTrustEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LandingTrustEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Icon = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandingTrustEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LandingTrustEntryTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LandingTrustEntryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LanguageCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandingTrustEntryTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LandingTrustEntryTranslations_LandingTrustEntries_LandingTrustEntryId",
                        column: x => x.LandingTrustEntryId,
                        principalTable: "LandingTrustEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LandingTrustEntries_IsPublished_SortOrder",
                table: "LandingTrustEntries",
                columns: new[] { "IsPublished", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_LandingTrustEntryTranslations_LandingTrustEntryId_LanguageCode",
                table: "LandingTrustEntryTranslations",
                columns: new[] { "LandingTrustEntryId", "LanguageCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LandingTrustEntryTranslations");

            migrationBuilder.DropTable(
                name: "LandingTrustEntries");
        }
    }
}
