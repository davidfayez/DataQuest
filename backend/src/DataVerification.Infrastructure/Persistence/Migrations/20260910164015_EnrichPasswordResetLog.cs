using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class EnrichPasswordResetLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AutonomousSystem",
                table: "PasswordResetRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Continent",
                table: "PasswordResetRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContinentCode",
                table: "PasswordResetRequests",
                type: "nvarchar(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "PasswordResetRequests",
                type: "nvarchar(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "PasswordResetRequests",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHosting",
                table: "PasswordResetRequests",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsMobileNetwork",
                table: "PasswordResetRequests",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsProxy",
                table: "PasswordResetRequests",
                type: "bit",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Isp",
                table: "PasswordResetRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "PasswordResetRequests",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "PasswordResetRequests",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Organisation",
                table: "PasswordResetRequests",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PostalCode",
                table: "PasswordResetRequests",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "PasswordResetRequests",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RegionName",
                table: "PasswordResetRequests",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReverseDns",
                table: "PasswordResetRequests",
                type: "nvarchar(253)",
                maxLength: 253,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimeZone",
                table: "PasswordResetRequests",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UtcOffsetSeconds",
                table: "PasswordResetRequests",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutonomousSystem",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "Continent",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "ContinentCode",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "District",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "IsHosting",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "IsMobileNetwork",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "IsProxy",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "Isp",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "Organisation",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "PostalCode",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "RegionName",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "ReverseDns",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "TimeZone",
                table: "PasswordResetRequests");

            migrationBuilder.DropColumn(
                name: "UtcOffsetSeconds",
                table: "PasswordResetRequests");
        }
    }
}
