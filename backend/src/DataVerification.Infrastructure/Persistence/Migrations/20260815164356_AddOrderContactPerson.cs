using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOrderContactPerson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContactPersonName",
                table: "Orders",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPersonPhoneCode",
                table: "Orders",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPersonPhoneCountry",
                table: "Orders",
                type: "nchar(2)",
                fixedLength: true,
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContactPersonPhoneNumber",
                table: "Orders",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContactPersonName",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ContactPersonPhoneCode",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ContactPersonPhoneCountry",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "ContactPersonPhoneNumber",
                table: "Orders");
        }
    }
}
