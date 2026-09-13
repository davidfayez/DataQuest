using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneCodeAndTransactionTypeCountries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TransactionTypes_Countries_CountryId",
                table: "TransactionTypes");

            migrationBuilder.DropIndex(
                name: "IX_TransactionTypes_CountryId_IsActive",
                table: "TransactionTypes");

            migrationBuilder.AddColumn<string>(
                name: "PhoneCode",
                table: "Countries",
                type: "nvarchar(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "TransactionTypeCountries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TransactionTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CountryId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransactionTypeCountries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransactionTypeCountries_Countries_CountryId",
                        column: x => x.CountryId,
                        principalTable: "Countries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TransactionTypeCountries_TransactionTypes_TransactionTypeId",
                        column: x => x.TransactionTypeId,
                        principalTable: "TransactionTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Preserve the previous single-country link before the FK column is removed.
            //
            // Wrapped in EXEC so TransactionTypes.CountryId is resolved when the statement runs
            // rather than when the batch is compiled. `dotnet ef migrations script --idempotent`
            // emits a whole migration as one batch, and SQL Server compiles a batch before running
            // any of it: on a database that already has this migration the column is gone, and the
            // batch fails with "Invalid column name 'CountryId'" even though the guard around it
            // would have skipped the statement entirely.
            migrationBuilder.Sql("""
                EXEC(N'
                    INSERT INTO TransactionTypeCountries (Id, TransactionTypeId, CountryId, CreatedAtUtc, UpdatedAtUtc)
                    SELECT NEWID(), Id, CountryId, CreatedAtUtc, UpdatedAtUtc
                    FROM TransactionTypes;
                ');
                """);

            migrationBuilder.DropColumn(
                name: "CountryId",
                table: "TransactionTypes");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionTypeCountries_CountryId_TransactionTypeId",
                table: "TransactionTypeCountries",
                columns: new[] { "CountryId", "TransactionTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_TransactionTypeCountries_TransactionTypeId_CountryId",
                table: "TransactionTypeCountries",
                columns: new[] { "TransactionTypeId", "CountryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CountryId",
                table: "TransactionTypes",
                type: "uniqueidentifier",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Best-effort restore: keep one country per transaction type.
            migrationBuilder.Sql("""
                UPDATE tt
                SET tt.CountryId = link.CountryId
                FROM TransactionTypes tt
                INNER JOIN (
                    SELECT TransactionTypeId, MIN(CONVERT(nvarchar(36), CountryId)) AS CountryIdText
                    FROM TransactionTypeCountries
                    GROUP BY TransactionTypeId
                ) picked ON picked.TransactionTypeId = tt.Id
                INNER JOIN TransactionTypeCountries link
                    ON link.TransactionTypeId = picked.TransactionTypeId
                    AND CONVERT(nvarchar(36), link.CountryId) = picked.CountryIdText;
                """);

            migrationBuilder.DropTable(
                name: "TransactionTypeCountries");

            migrationBuilder.DropColumn(
                name: "PhoneCode",
                table: "Countries");

            migrationBuilder.CreateIndex(
                name: "IX_TransactionTypes_CountryId_IsActive",
                table: "TransactionTypes",
                columns: new[] { "CountryId", "IsActive" });

            migrationBuilder.AddForeignKey(
                name: "FK_TransactionTypes_Countries_CountryId",
                table: "TransactionTypes",
                column: "CountryId",
                principalTable: "Countries",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
