using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentMethodRequiredDocuments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DocumentNameAr",
                table: "WalletRequestFiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DocumentNameEn",
                table: "WalletRequestFiles",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RequiredFileId",
                table: "WalletRequestFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ServiceTypeId",
                table: "ServiceTypeRequiredFiles",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier");

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentMethodId",
                table: "ServiceTypeRequiredFiles",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WalletRequestDocumentValues",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WalletRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequiredFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    RequiredFileFieldId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DocumentNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DocumentNameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FieldNameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    FieldNameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Value = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    ValueLabelAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ValueLabelEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletRequestDocumentValues", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletRequestDocumentValues_WalletRequests_WalletRequestId",
                        column: x => x.WalletRequestId,
                        principalTable: "WalletRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypeRequiredFiles_PaymentMethodId",
                table: "ServiceTypeRequiredFiles",
                column: "PaymentMethodId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServiceTypeRequiredFiles_SingleOwner",
                table: "ServiceTypeRequiredFiles",
                sql: "([ServiceTypeId] IS NOT NULL AND [PaymentMethodId] IS NULL) OR ([ServiceTypeId] IS NULL AND [PaymentMethodId] IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_WalletRequestDocumentValues_RequiredFileId",
                table: "WalletRequestDocumentValues",
                column: "RequiredFileId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletRequestDocumentValues_WalletRequestId",
                table: "WalletRequestDocumentValues",
                column: "WalletRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceTypeRequiredFiles_PaymentMethods_PaymentMethodId",
                table: "ServiceTypeRequiredFiles",
                column: "PaymentMethodId",
                principalTable: "PaymentMethods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceTypeRequiredFiles_PaymentMethods_PaymentMethodId",
                table: "ServiceTypeRequiredFiles");

            migrationBuilder.DropTable(
                name: "WalletRequestDocumentValues");

            migrationBuilder.DropIndex(
                name: "IX_ServiceTypeRequiredFiles_PaymentMethodId",
                table: "ServiceTypeRequiredFiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServiceTypeRequiredFiles_SingleOwner",
                table: "ServiceTypeRequiredFiles");

            migrationBuilder.DropColumn(
                name: "DocumentNameAr",
                table: "WalletRequestFiles");

            migrationBuilder.DropColumn(
                name: "DocumentNameEn",
                table: "WalletRequestFiles");

            migrationBuilder.DropColumn(
                name: "RequiredFileId",
                table: "WalletRequestFiles");

            migrationBuilder.DropColumn(
                name: "PaymentMethodId",
                table: "ServiceTypeRequiredFiles");

            migrationBuilder.AlterColumn<Guid>(
                name: "ServiceTypeId",
                table: "ServiceTypeRequiredFiles",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
