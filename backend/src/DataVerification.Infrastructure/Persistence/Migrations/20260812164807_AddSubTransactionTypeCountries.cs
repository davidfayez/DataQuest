using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSubTransactionTypeCountries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubTransactionTypeCountries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SubTransactionTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubTransactionTypeCountries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SubTransactionTypeCountries_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SubTransactionTypeCountries_SubTransactionTypes_SubTransactionTypeId",
                        column: x => x.SubTransactionTypeId,
                        principalTable: "SubTransactionTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubTransactionTypeCountries_CountryId_SubTransactionTypeId",
                table: "SubTransactionTypeCountries",
                columns: new[] { "CountryId", "SubTransactionTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubTransactionTypeCountries_SubTransactionTypeId_CountryId",
                table: "SubTransactionTypeCountries",
                columns: new[] { "SubTransactionTypeId", "CountryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubTransactionTypeCountries");
        }
    }
}
