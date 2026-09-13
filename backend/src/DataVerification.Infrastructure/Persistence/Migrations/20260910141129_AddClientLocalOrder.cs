using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClientLocalOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLocalOrder",
                table: "Clients",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_Clients_IsLocalOrder",
                table: "Clients",
                column: "IsLocalOrder",
                unique: true,
                filter: "[IsLocalOrder] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Clients_IsLocalOrder",
                table: "Clients");

            migrationBuilder.DropColumn(
                name: "IsLocalOrder",
                table: "Clients");
        }
    }
}
