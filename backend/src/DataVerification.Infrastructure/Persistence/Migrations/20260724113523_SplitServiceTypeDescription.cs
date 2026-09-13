using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SplitServiceTypeDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The old single Description held Arabic-only copy; dropping it lets the seeder backfill
            // both languages correctly rather than stranding Arabic text in the English column.
            migrationBuilder.DropColumn(
                name: "Description",
                table: "ServiceTypes");

            migrationBuilder.AddColumn<string>(
                name: "DescriptionAr",
                table: "ServiceTypes",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DescriptionEn",
                table: "ServiceTypes",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DescriptionAr",
                table: "ServiceTypes");

            migrationBuilder.DropColumn(
                name: "DescriptionEn",
                table: "ServiceTypes");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "ServiceTypes",
                type: "nvarchar(2000)",
                maxLength: 2000,
                nullable: true);
        }
    }
}
