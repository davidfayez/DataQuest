using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddApplicationApplicantContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApplicantEmail",
                table: "Applications",
                type: "nvarchar(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicantPhoneCode",
                table: "Applications",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicantPhoneCountry",
                table: "Applications",
                type: "nchar(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApplicantPhoneNumber",
                table: "Applications",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApplicantEmail",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "ApplicantPhoneCode",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "ApplicantPhoneCountry",
                table: "Applications");

            migrationBuilder.DropColumn(
                name: "ApplicantPhoneNumber",
                table: "Applications");
        }
    }
}
