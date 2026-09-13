using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAttachedDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ApplicationDocumentId",
                table: "ApplicationFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ApplicationDocuments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AttachedAtStatus = table.Column<int>(type: "int", nullable: true),
                    IsVisibleToApplicant = table.Column<bool>(type: "bit", nullable: false),
                    UploadedByType = table.Column<int>(type: "int", nullable: false),
                    UploadedById = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UploadedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationDocuments_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationDocumentFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationDocumentId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldType = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    MinLength = table.Column<int>(type: "int", nullable: true),
                    MaxLength = table.Column<int>(type: "int", nullable: true),
                    Pattern = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    MinValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 4, nullable: true),
                    MaxValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 4, nullable: true),
                    DateRule = table.Column<int>(type: "int", nullable: false),
                    MinDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MaxDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationDocumentFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationDocumentFields_ApplicationDocuments_ApplicationDocumentId",
                        column: x => x.ApplicationDocumentId,
                        principalTable: "ApplicationDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationDocumentFieldOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationDocumentFieldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LabelAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LabelEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationDocumentFieldOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationDocumentFieldOptions_ApplicationDocumentFields_ApplicationDocumentFieldId",
                        column: x => x.ApplicationDocumentFieldId,
                        principalTable: "ApplicationDocumentFields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationFiles_ApplicationDocumentId",
                table: "ApplicationFiles",
                column: "ApplicationDocumentId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocumentFieldOptions_ApplicationDocumentFieldId_SortOrder",
                table: "ApplicationDocumentFieldOptions",
                columns: new[] { "ApplicationDocumentFieldId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocumentFields_ApplicationDocumentId_SortOrder",
                table: "ApplicationDocumentFields",
                columns: new[] { "ApplicationDocumentId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocuments_ApplicationId_IsVisibleToApplicant_SortOrder",
                table: "ApplicationDocuments",
                columns: new[] { "ApplicationId", "IsVisibleToApplicant", "SortOrder" });

            migrationBuilder.AddForeignKey(
                name: "FK_ApplicationFiles_ApplicationDocuments_ApplicationDocumentId",
                table: "ApplicationFiles",
                column: "ApplicationDocumentId",
                principalTable: "ApplicationDocuments",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ApplicationFiles_ApplicationDocuments_ApplicationDocumentId",
                table: "ApplicationFiles");

            migrationBuilder.DropTable(
                name: "ApplicationDocumentFieldOptions");

            migrationBuilder.DropTable(
                name: "ApplicationDocumentFields");

            migrationBuilder.DropTable(
                name: "ApplicationDocuments");

            migrationBuilder.DropIndex(
                name: "IX_ApplicationFiles_ApplicationDocumentId",
                table: "ApplicationFiles");

            migrationBuilder.DropColumn(
                name: "ApplicationDocumentId",
                table: "ApplicationFiles");
        }
    }
}
