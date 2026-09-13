using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentMethodIntegrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentMethodIntegrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Provider = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: true),
                    Mode = table.Column<int>(type: "int", nullable: false),
                    MerchantId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    IntegrationId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    ApiKeySecret = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    PasswordSecret = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    WebhookSecret = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    BaseUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    RedirectUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CancelUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CallbackUrl = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    SessionTimeoutMinutes = table.Column<int>(type: "int", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethodIntegrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentMethodIntegrations_PaymentMethods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalTable: "PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethodIntegrations_PaymentMethodId",
                table: "PaymentMethodIntegrations",
                column: "PaymentMethodId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentMethodIntegrations");
        }
    }
}
