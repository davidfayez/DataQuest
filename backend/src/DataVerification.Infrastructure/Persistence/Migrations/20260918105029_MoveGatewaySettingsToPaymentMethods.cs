using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MoveGatewaySettingsToPaymentMethods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SecretsJson",
                table: "PaymentGatewayIntegrations");

            migrationBuilder.DropColumn(
                name: "SettingsJson",
                table: "PaymentGatewayIntegrations");

            migrationBuilder.AddColumn<string>(
                name: "GatewaySecretsJson",
                table: "PaymentMethods",
                type: "nvarchar(max)",
                nullable: false,
                // An empty JSON object, which is what a method with no gateway settings holds.
                defaultValue: "{}");

            migrationBuilder.AddColumn<string>(
                name: "GatewaySettingsJson",
                table: "PaymentMethods",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "{}");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GatewaySecretsJson",
                table: "PaymentMethods");

            migrationBuilder.DropColumn(
                name: "GatewaySettingsJson",
                table: "PaymentMethods");

            migrationBuilder.AddColumn<string>(
                name: "SecretsJson",
                table: "PaymentGatewayIntegrations",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SettingsJson",
                table: "PaymentGatewayIntegrations",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
