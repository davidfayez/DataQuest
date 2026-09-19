using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiCurrencyWallets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Wallets_OrderId",
                table: "Wallets");

            migrationBuilder.AddColumn<Guid>(
                name: "CurrencyId",
                table: "Applications",
                type: "uniqueidentifier",
                nullable: true);

            // Every existing application was priced in its order's currency: record it.
            migrationBuilder.Sql("""
                UPDATE a
                SET CurrencyId = o.CurrencyId
                FROM Applications a
                JOIN Orders o ON o.Id = a.OrderId
                WHERE a.CurrencyId IS NULL AND o.CurrencyId IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_OrderId_CurrencyId",
                table: "Wallets",
                columns: new[] { "OrderId", "CurrencyId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Applications_CurrencyId",
                table: "Applications",
                column: "CurrencyId");

            migrationBuilder.AddForeignKey(
                name: "FK_Applications_Currencies_CurrencyId",
                table: "Applications",
                column: "CurrencyId",
                principalTable: "Currencies",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Applications_Currencies_CurrencyId",
                table: "Applications");

            migrationBuilder.DropIndex(
                name: "IX_Wallets_OrderId_CurrencyId",
                table: "Wallets");

            migrationBuilder.DropIndex(
                name: "IX_Applications_CurrencyId",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "CurrencyId",
                table: "Applications");

            migrationBuilder.CreateIndex(
                name: "IX_Wallets_OrderId",
                table: "Wallets",
                column: "OrderId",
                unique: true);
        }
    }
}
