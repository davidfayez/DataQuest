using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequiredDocumentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Every existing document accepts exactly one file, which is what the wizard allowed
            // before this column existed. Defaulting to 0 would read as "no files permitted" and
            // make every configured service unsubmittable.
            migrationBuilder.AddColumn<int>(
                name: "MaxFiles",
                table: "ServiceTypeRequiredFiles",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<long>(
                name: "MaxSizeBytes",
                table: "ServiceTypeRequiredFiles",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "RequiredFileFields",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequiredFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldType = table.Column<int>(type: "int", nullable: false),
                    IsRequired = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    MinLength = table.Column<int>(type: "int", nullable: true),
                    MaxLength = table.Column<int>(type: "int", nullable: true),
                    Pattern = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    MinValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 4, nullable: true),
                    MaxValue = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 4, nullable: true),
                    DateRule = table.Column<int>(type: "int", nullable: false),
                    MinDate = table.Column<DateOnly>(type: "date", nullable: true),
                    MaxDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequiredFileFields", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequiredFileFields_ServiceTypeRequiredFiles_RequiredFileId",
                        column: x => x.RequiredFileId,
                        principalTable: "ServiceTypeRequiredFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApplicationDocumentValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ApplicationServiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequiredFileFieldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApplicationDocumentValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApplicationDocumentValues_ApplicationServices_ApplicationServiceId",
                        column: x => x.ApplicationServiceId,
                        principalTable: "ApplicationServices",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ApplicationDocumentValues_Applications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "Applications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ApplicationDocumentValues_RequiredFileFields_RequiredFileFieldId",
                        column: x => x.RequiredFileFieldId,
                        principalTable: "RequiredFileFields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RequiredFileFieldOptions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequiredFileFieldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LabelAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    LabelEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequiredFileFieldOptions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RequiredFileFieldOptions_RequiredFileFields_RequiredFileFieldId",
                        column: x => x.RequiredFileFieldId,
                        principalTable: "RequiredFileFields",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocumentValues_ApplicationId",
                table: "ApplicationDocumentValues",
                column: "ApplicationId");

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocumentValues_ApplicationServiceId_RequiredFileFieldId",
                table: "ApplicationDocumentValues",
                columns: new[] { "ApplicationServiceId", "RequiredFileFieldId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApplicationDocumentValues_RequiredFileFieldId",
                table: "ApplicationDocumentValues",
                column: "RequiredFileFieldId");

            migrationBuilder.CreateIndex(
                name: "IX_RequiredFileFieldOptions_RequiredFileFieldId_SortOrder",
                table: "RequiredFileFieldOptions",
                columns: new[] { "RequiredFileFieldId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_RequiredFileFields_IsActive",
                table: "RequiredFileFields",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_RequiredFileFields_RequiredFileId_SortOrder",
                table: "RequiredFileFields",
                columns: new[] { "RequiredFileId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApplicationDocumentValues");

            migrationBuilder.DropTable(
                name: "RequiredFileFieldOptions");

            migrationBuilder.DropTable(
                name: "RequiredFileFields");

            migrationBuilder.DropColumn(
                name: "MaxFiles",
                table: "ServiceTypeRequiredFiles");

            migrationBuilder.DropColumn(
                name: "MaxSizeBytes",
                table: "ServiceTypeRequiredFiles");
        }
    }
}
