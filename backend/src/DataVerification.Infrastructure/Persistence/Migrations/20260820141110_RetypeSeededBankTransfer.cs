using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetypeSeededBankTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The seeded "Bank Transfer" type was created before a bank-transfer kind existed, so
            // it landed as a wallet transfer. Corrected here — but only while nothing is configured
            // against it: retyping a type in use would demand a bank on receiving rows that have
            // none, and quietly hide a working method from applicants.
            migrationBuilder.Sql(@"
                UPDATE [PaymentMethodTypes]
                SET [Kind] = 3
                WHERE [Id] = 'AAAAAAAA-1111-0000-0000-000000000003'
                  AND [Kind] = 1
                  AND NOT EXISTS (
                      SELECT 1 FROM [PaymentMethods] m
                      WHERE m.[PaymentMethodTypeId] = [PaymentMethodTypes].[Id]);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                UPDATE [PaymentMethodTypes]
                SET [Kind] = 1
                WHERE [Id] = 'AAAAAAAA-1111-0000-0000-000000000003' AND [Kind] = 3;");
        }
    }
}
