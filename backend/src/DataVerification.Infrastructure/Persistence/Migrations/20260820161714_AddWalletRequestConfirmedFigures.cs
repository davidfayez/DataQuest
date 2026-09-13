using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWalletRequestConfirmedFigures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ConfirmedAmount",
                table: "WalletRequests",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ConfirmedReference",
                table: "WalletRequests",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalletRequests_ConfirmedReference",
                table: "WalletRequests",
                column: "ConfirmedReference",
                unique: true,
                filter: "[ConfirmedReference] IS NOT NULL AND [Status] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WalletRequests_ConfirmedReference",
                table: "WalletRequests");

            migrationBuilder.DropColumn(
                name: "ConfirmedAmount",
                table: "WalletRequests");

            migrationBuilder.DropColumn(
                name: "ConfirmedReference",
                table: "WalletRequests");
        }
    }
}
