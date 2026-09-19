using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCountryMainCurrency : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsDefault",
                table: "CountryCurrencies",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // Every country with currencies gets a main one: the currency shared by the fewest
            // countries (so Egypt lands on EGP rather than USD), then the lowest code. Done before
            // the index, which allows only one per country.
            migrationBuilder.Sql("""
                WITH Shared AS (
                    SELECT CurrencyId, COUNT(*) AS Countries
                    FROM CountryCurrencies
                    GROUP BY CurrencyId
                ),
                Ranked AS (
                    SELECT cc.Id,
                           ROW_NUMBER() OVER (
                               PARTITION BY cc.CountryId
                               ORDER BY s.Countries, c.Code) AS Rank
                    FROM CountryCurrencies cc
                    JOIN Shared s ON s.CurrencyId = cc.CurrencyId
                    JOIN Currencies c ON c.Id = cc.CurrencyId
                )
                UPDATE cc
                SET IsDefault = 1
                FROM CountryCurrencies cc
                JOIN Ranked r ON r.Id = cc.Id
                WHERE r.Rank = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_CountryCurrencies_MainPerCountry",
                table: "CountryCurrencies",
                column: "CountryId",
                unique: true,
                filter: "[IsDefault] = 1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CountryCurrencies_MainPerCountry",
                table: "CountryCurrencies");

            migrationBuilder.DropColumn(
                name: "IsDefault",
                table: "CountryCurrencies");
        }
    }
}
