using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLookupDescriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "VerificationAuthorities",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionEn",
                table: "VerificationAuthorities",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "TransactionTypes",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionEn",
                table: "TransactionTypes",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "SubTransactionTypes",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionEn",
                table: "SubTransactionTypes",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "VerificationAuthorities");

            migrationBuilder.DropColumn(
                name: "DescriptionEn",
                table: "VerificationAuthorities");

            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "TransactionTypes");

            migrationBuilder.DropColumn(
                name: "DescriptionEn",
                table: "TransactionTypes");

            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "SubTransactionTypes");

            migrationBuilder.DropColumn(
                name: "DescriptionEn",
                table: "SubTransactionTypes");
        }
    }
}
