using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequiredFileAllowedTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RequiredFileAllowedTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequiredFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileTypeCode = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequiredFileAllowedTypes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequiredFileAllowedTypes_ServiceTypeRequiredFiles_RequiredFileId",
                        column: x => x.RequiredFileId,
                        principalTable: "ServiceTypeRequiredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RequiredFileAllowedTypes_RequiredFileId_FileTypeCode",
                table: "RequiredFileAllowedTypes",
                columns: new[] { "RequiredFileId", "FileTypeCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RequiredFileAllowedTypes");
        }
    }
}
