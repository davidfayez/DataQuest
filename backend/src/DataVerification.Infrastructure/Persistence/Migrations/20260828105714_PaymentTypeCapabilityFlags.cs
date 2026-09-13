using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Turns the four "what does this provider need" questions from consequences of the kind into
    /// columns an operator sets, and collapses the kind itself to Transfer or PayPal.
    /// </summary>
    /// <remarks>
    /// The column additions are the easy half. The half that matters is carrying the old meaning
    /// across: every existing type had its requirements implied by its kind, and a type that comes
    /// out of this migration with all four flags false makes every method on it unusable — which is
    /// to say it takes the platform's payment options offline. So each old kind is written into the
    /// flags it used to imply, and only then is the kind collapsed.
    ///
    /// The old values: 0 external link, 1 wallet transfer, 2 InstaPay, 3 bank transfer.
    /// </remarks>
    public partial class PaymentTypeCapabilityFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RequiresAccountNumber",
                table: "PaymentMethodTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresBank",
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

            migrationBuilder.AddColumn<bool>(
                name: "RequiresExternalUrl",
                table: "PaymentMethodTypes",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Each old kind, written into the flags it used to stand for.
            //
            // Through EXEC because it writes the four columns added immediately above. An
            // idempotent script puts a whole migration in one batch and SQL Server compiles the
            // batch before running any of it; name resolution is deferred for tables created in
            // the batch but not for columns added to an existing one, so a script run against an
            // empty database would stop here with "Invalid column name 'RequiresAccountNumber'".
            migrationBuilder.Sql(@"
                EXEC(N'
                    UPDATE [PaymentMethodTypes]
                    SET [RequiresAccountNumber] = CASE WHEN [Kind] IN (1, 2, 3) THEN 1 ELSE 0 END,
                        [RequiresBarcode]       = CASE WHEN [Kind] = 2 THEN 1 ELSE 0 END,
                        [RequiresBank]          = CASE WHEN [Kind] = 3 THEN 1 ELSE 0 END,
                        [RequiresExternalUrl]   = CASE WHEN [Kind] = 0 THEN 1 ELSE 0 END;
                ');");

            // Only now is it safe to lose the distinction. An external link becomes a transfer that
            // needs a link: the applicant still pays out of band and an administrator still
            // confirms it, which is what Transfer means.
            migrationBuilder.Sql(@"
                UPDATE [PaymentMethodTypes]
                SET [Kind] = 1
                WHERE [Kind] IN (0, 2, 3);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Put the kinds back before the flags that describe them are dropped, or the
            // information needed to reconstruct them goes with the columns.
            migrationBuilder.Sql(@"
                UPDATE [PaymentMethodTypes]
                SET [Kind] = CASE
                        WHEN [RequiresExternalUrl] = 1 THEN 0
                        WHEN [RequiresBarcode] = 1 THEN 2
                        WHEN [RequiresBank] = 1 THEN 3
                        ELSE 1
                    END
                WHERE [Kind] <> 4;");

            // A PayPal type has no equivalent in the old scheme; the nearest thing an applicant
            // could be shown is an external link, which is where its methods came from.
            migrationBuilder.Sql(@"
                UPDATE [PaymentMethodTypes] SET [Kind] = 0 WHERE [Kind] = 4;");

            migrationBuilder.DropColumn(
                name: "RequiresAccountNumber",
                table: "PaymentMethodTypes");

            migrationBuilder.DropColumn(
                name: "RequiresBank",
                table: "PaymentMethodTypes");

            migrationBuilder.DropColumn(
                name: "RequiresBarcode",
                table: "PaymentMethodTypes");

            migrationBuilder.DropColumn(
                name: "RequiresExternalUrl",
                table: "PaymentMethodTypes");
        }
    }
}
