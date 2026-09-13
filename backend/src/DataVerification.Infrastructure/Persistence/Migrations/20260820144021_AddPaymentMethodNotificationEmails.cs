using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPaymentMethodNotificationEmails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PaymentMethodNotificationEmails",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PaymentMethodId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    NotifyOnSubmitted = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    NotifyOnApproved = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    NotifyOnRejected = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentMethodNotificationEmails", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentMethodNotificationEmails_PaymentMethods_PaymentMethodId",
                        column: x => x.PaymentMethodId,
                        principalTable: "PaymentMethods",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentMethodNotificationEmails_PaymentMethodId_Email",
                table: "PaymentMethodNotificationEmails",
                columns: new[] { "PaymentMethodId", "Email" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentMethodNotificationEmails");
        }
    }
}
