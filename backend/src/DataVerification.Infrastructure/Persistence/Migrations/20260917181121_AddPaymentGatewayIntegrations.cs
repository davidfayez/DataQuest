using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentGatewayIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "GatewayIntegrationId",
                table: "PaymentMethods",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PaymentGatewayIntegrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GatewayCode = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    SettingsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SecretsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true),
                    NameAr = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    NameEn = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    DescriptionAr = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    DescriptionEn = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentGatewayIntegrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethods_GatewayIntegrationId",
                table: "PaymentMethods",
                column: "GatewayIntegrationId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentGatewayIntegrations_GatewayCode_IsActive",
                table: "PaymentGatewayIntegrations",
                columns: new[] { "GatewayCode", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentGatewayIntegrations_IsActive",
                table: "PaymentGatewayIntegrations",
                column: "IsActive");

            migrationBuilder.AddForeignKey(
                name: "FK_PaymentMethods_PaymentGatewayIntegrations_GatewayIntegrationId",
                table: "PaymentMethods",
                column: "GatewayIntegrationId",
                principalTable: "PaymentGatewayIntegrations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PaymentMethods_PaymentGatewayIntegrations_GatewayIntegrationId",
                table: "PaymentMethods");

            migrationBuilder.DropTable(
                name: "PaymentGatewayIntegrations");

            migrationBuilder.DropIndex(
                name: "IX_PaymentMethods_GatewayIntegrationId",
                table: "PaymentMethods");

            migrationBuilder.DropColumn(
                name: "GatewayIntegrationId",
                table: "PaymentMethods");
        }
    }
}
