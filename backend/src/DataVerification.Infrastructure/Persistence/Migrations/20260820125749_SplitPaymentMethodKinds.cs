using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitPaymentMethodKinds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The single "Transfer" kind (1) splits into WalletTransfer (1) and InstaPayTransfer
            // (2). Which one a row becomes is exactly what its RequiresBarcode flag already said,
            // so the data moves across before the flag is dropped — afterwards the answer is gone.
            migrationBuilder.Sql(@"
                UPDATE [PaymentMethodTypes]
                SET [Kind] = 2
                WHERE [Kind] = 1 AND [RequiresBarcode] = 1;");

            // A link never had receiving numbers; anything else that claimed none was a transfer
            // that could not work, and is left as a wallet transfer for an operator to look at.
            migrationBuilder.DropColumn(
                name: "RequiresAccountNumber",
                table: "PaymentMethodTypes");

            migrationBuilder.DropColumn(
                name: "RequiresBarcode",
                table: "PaymentMethodTypes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresAccountNumber",
                table: "PaymentMethodTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresBarcode",
                table: "PaymentMethodTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Rebuild the flags from the kind, then fold InstaPay back into the single Transfer.
            migrationBuilder.Sql(@"
                UPDATE [PaymentMethodTypes]
                SET [RequiresAccountNumber] = CASE WHEN [Kind] IN (1, 2) THEN 1 ELSE 0 END,
                    [RequiresBarcode]       = CASE WHEN [Kind] = 2 THEN 1 ELSE 0 END;");

            migrationBuilder.Sql(@"
                UPDATE [PaymentMethodTypes]
                SET [Kind] = 1
                WHERE [Kind] = 2;");
        }
    }
}
