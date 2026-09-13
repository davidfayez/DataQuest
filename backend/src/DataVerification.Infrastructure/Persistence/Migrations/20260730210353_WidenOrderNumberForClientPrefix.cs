using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class WidenOrderNumberForClientPrefix : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The unique index has to come off first: SQL Server refuses to change the data type of
            // an indexed column, and nchar(12) -> nvarchar(29) is a type change rather than a plain
            // widening. Every existing number is exactly 12 characters, so nothing is truncated.
            migrationBuilder.DropIndex(
                name: "IX_Orders_OrderNumber",
                table: "Orders");

            migrationBuilder.AlterColumn<string>(
                name: "OrderNumber",
                table: "Orders",
                type: "nvarchar(29)",
                maxLength: 29,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nchar(12)",
                oldFixedLength: true,
                oldMaxLength: 12);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderNumber",
                table: "Orders",
                column: "OrderNumber",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_OrderNumber",
                table: "Orders");

            // Reverting only fits numbers of 12 characters or fewer, so it is safe before any order
            // has been issued under the client-prefixed format and lossy afterwards.
            migrationBuilder.AlterColumn<string>(
                name: "OrderNumber",
                table: "Orders",
                type: "nchar(12)",
                fixedLength: true,
                maxLength: 12,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(29)",
                oldMaxLength: 29);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_OrderNumber",
                table: "Orders",
                column: "OrderNumber",
                unique: true);
        }
    }
}
