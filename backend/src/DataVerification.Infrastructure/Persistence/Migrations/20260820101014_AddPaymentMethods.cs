using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "PaymentMethodAccountId",
                table: "WalletRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PaymentMethodId",
                table: "WalletRequests",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReferenceNumber",
                table: "WalletRequests",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentMethodTypes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    RequiresAccountNumber = table.Column<bool>(type: "bit", nullable: false),
                    RequiresBarcode = table.Column<bool>(type: "bit", nullable: false),
                    RequiresProofDocument = table.Column<bool>(type: "bit", nullable: false),
                    RequiresReferenceNumber = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethodTypes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WalletRequestFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WalletRequestId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    UploadedByName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletRequestFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WalletRequestFiles_WalletRequests_WalletRequestId",
                        column: x => x.WalletRequestId,
                        principalTable: "WalletRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethods",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentMethodTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DescriptionAr = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PublicNoteAr = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PublicNoteEn = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PrivateNoteAr = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PrivateNoteEn = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ExternalUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethods", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentMethods_PaymentMethodTypes_PaymentMethodTypeId",
                        column: x => x.PaymentMethodTypeId,
                        principalTable: "PaymentMethodTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethodAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LabelAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    LabelEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: ""),
                    AccountNumber = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    AccountHolder = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    BarcodeStoragePath = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BarcodeContentType = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    BarcodeFileName = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethodAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentMethodAccounts_PaymentMethods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalTable: "PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethodCountries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethodCountries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentMethodCountries_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentMethodCountries_PaymentMethods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalTable: "PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PaymentMethodCurrencies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethodCurrencies", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentMethodCurrencies_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentMethodCurrencies_PaymentMethods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalTable: "PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalletRequests_PaymentMethodAccountId",
                table: "WalletRequests",
                column: "PaymentMethodAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletRequests_PaymentMethodId",
                table: "WalletRequests",
                column: "PaymentMethodId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletRequests_ReferenceNumber",
                table: "WalletRequests",
                column: "ReferenceNumber",
                unique: true,
                filter: "[ReferenceNumber] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethodAccounts_PaymentMethodId_AccountNumber",
                table: "PaymentMethodAccounts",
                columns: new[] { "PaymentMethodId", "AccountNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethodAccounts_PaymentMethodId_SortOrder",
                table: "PaymentMethodAccounts",
                columns: new[] { "PaymentMethodId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethodCountries_CountryId",
                table: "PaymentMethodCountries",
                column: "CountryId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethodCountries_PaymentMethodId_CountryId",
                table: "PaymentMethodCountries",
                columns: new[] { "PaymentMethodId", "CountryId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethodCurrencies_CurrencyId",
                table: "PaymentMethodCurrencies",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethodCurrencies_PaymentMethodId_CurrencyId",
                table: "PaymentMethodCurrencies",
                columns: new[] { "PaymentMethodId", "CurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_IsActive",
                table: "PaymentMethods",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_PaymentMethodTypeId_IsActive",
                table: "PaymentMethods",
                columns: new[] { "PaymentMethodTypeId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_SortOrder",
                table: "PaymentMethods",
                column: "SortOrder");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethodTypes_IsActive",
                table: "PaymentMethodTypes",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethodTypes_Kind_IsActive",
                table: "PaymentMethodTypes",
                columns: new[] { "Kind", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletRequestFiles_WalletRequestId",
                table: "WalletRequestFiles",
                column: "WalletRequestId");

            migrationBuilder.AddForeignKey(
                name: "FK_WalletRequests_PaymentMethodAccounts_PaymentMethodAccountId",
                table: "WalletRequests",
                column: "PaymentMethodAccountId",
                principalTable: "PaymentMethodAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WalletRequests_PaymentMethods_PaymentMethodId",
                table: "WalletRequests",
                column: "PaymentMethodId",
                principalTable: "PaymentMethods",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WalletRequests_PaymentMethodAccounts_PaymentMethodAccountId",
                table: "WalletRequests");

            migrationBuilder.DropForeignKey(
                name: "FK_WalletRequests_PaymentMethods_PaymentMethodId",
                table: "WalletRequests");

            migrationBuilder.DropTable(
                name: "PaymentMethodAccounts");

            migrationBuilder.DropTable(
                name: "PaymentMethodCountries");

            migrationBuilder.DropTable(
                name: "PaymentMethodCurrencies");

            migrationBuilder.DropTable(
                name: "WalletRequestFiles");

            migrationBuilder.DropTable(
                name: "PaymentMethods");

            migrationBuilder.DropTable(
                name: "PaymentMethodTypes");

            migrationBuilder.DropIndex(
                name: "IX_WalletRequests_PaymentMethodAccountId",
                table: "WalletRequests");

            migrationBuilder.DropIndex(
                name: "IX_WalletRequests_PaymentMethodId",
                table: "WalletRequests");

            migrationBuilder.DropIndex(
                name: "IX_WalletRequests_ReferenceNumber",
                table: "WalletRequests");

            migrationBuilder.DropColumn(
                name: "PaymentMethodAccountId",
                table: "WalletRequests");

            migrationBuilder.DropColumn(
                name: "PaymentMethodId",
                table: "WalletRequests");

            migrationBuilder.DropColumn(
                name: "ReferenceNumber",
                table: "WalletRequests");
        }
    }
}
