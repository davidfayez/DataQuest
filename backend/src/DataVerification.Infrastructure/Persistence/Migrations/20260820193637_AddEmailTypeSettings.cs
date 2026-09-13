using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEmailTypeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EmailTypeSettings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    FromAddress = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: true),
                    FromName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailTypeSettings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "EmailBccRecipients",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EmailTypeSettingId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailBccRecipients", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailBccRecipients_EmailTypeSettings_EmailTypeSettingId",
                        column: x => x.EmailTypeSettingId,
                        principalTable: "EmailTypeSettings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailBccRecipients_EmailTypeSettingId_Email",
                table: "EmailBccRecipients",
                columns: new[] { "EmailTypeSettingId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EmailTypeSettings_Type",
                table: "EmailTypeSettings",
                column: "Type",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailBccRecipients");

            migrationBuilder.DropTable(
                name: "EmailTypeSettings");
        }
    }
}
