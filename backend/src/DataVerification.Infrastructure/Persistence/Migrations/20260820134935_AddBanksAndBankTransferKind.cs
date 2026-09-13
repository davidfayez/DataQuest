using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBanksAndBankTransferKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BankId",
                table: "PaymentMethodAccounts",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Banks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SwiftCode = table.Column<string>(type: "nvarchar(11)", maxLength: 11, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Banks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Banks_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethodAccounts_BankId",
                table: "PaymentMethodAccounts",
                column: "BankId");

            migrationBuilder.CreateIndex(
                name: "IX_Banks_CountryId_NameEn",
                table: "Banks",
                columns: new[] { "CountryId", "NameEn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Banks_CountryId_SortOrder",
                table: "Banks",
                columns: new[] { "CountryId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Banks_IsActive",
                table: "Banks",
                column: "IsActive");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentMethodAccounts_Banks_BankId",
                table: "PaymentMethodAccounts",
                column: "BankId",
                principalTable: "Banks",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentMethodAccounts_Banks_BankId",
                table: "PaymentMethodAccounts");

            migrationBuilder.DropTable(
                name: "Banks");

            migrationBuilder.DropIndex(
                name: "IX_PaymentMethodAccounts_BankId",
                table: "PaymentMethodAccounts");

            migrationBuilder.DropColumn(
                name: "BankId",
                table: "PaymentMethodAccounts");
        }
    }
}
