using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RemoveIdentityDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationIdentities");

            migrationBuilder.DropTable(
                name: "IdentityTypes");

            // The catalogue no longer defines these, so leaving the rows behind would keep dead
            // permissions granted. The seeder prunes them too, but it only runs in Development.
            // Grants in RolePermissions and AdminUserPermissions cascade from this delete.
            migrationBuilder.Sql(
                "DELETE FROM [Permissions] WHERE [Name] LIKE 'IdentityTypes.%';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "IdentityTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IdentityTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CardNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiryDate = table.Column<DateOnly>(type: "date", nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationIdentities_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApplicationIdentities_IdentityTypes_IdentityTypeId",
                        column: x => x.IdentityTypeId,
                        principalTable: "IdentityTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationIdentities_ApplicationId",
                table: "ApplicationIdentities",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationIdentities_IdentityTypeId",
                table: "ApplicationIdentities",
                column: "IdentityTypeId");

            migrationBuilder.CreateIndex(
                name: "IX_IdentityTypes_IsActive",
                table: "IdentityTypes",
                column: "IsActive");
        }
    }
}
