using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class MultiLanguageLandingContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. New per-language table.
            migrationBuilder.CreateTable(
                name: "LandingFeatureTranslations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LandingFeatureId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LanguageCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Body = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LandingFeatureTranslations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LandingFeatureTranslations_LandingFeatures_LandingFeatureId",
                        column: x => x.LandingFeatureId,
                        principalTable: "LandingFeatures",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LandingFeatureTranslations_LandingFeatureId_LanguageCode",
                table: "LandingFeatureTranslations",
                columns: new[] { "LandingFeatureId", "LanguageCode" },
                unique: true);

            // 2. Preserve the existing single-language copy as the English translation.
            //
            // Wrapped in EXEC so LandingFeatures.Title and .Body are resolved when the statement
            // runs rather than when the batch is compiled — the same reason as the CountryId move
            // in AddPhoneCodeAndTransactionTypeCountries. Without it, an idempotent script fails
            // with "Invalid column name 'Title'" on every database that already has this
            // migration, because the two columns are dropped a few lines below.
            migrationBuilder.Sql(
                """
                EXEC(N'
                    INSERT INTO LandingFeatureTranslations (Id, LandingFeatureId, LanguageCode, Title, Body, CreatedAtUtc, UpdatedAtUtc)
                    SELECT NEWID(), Id, ''en'', Title, Body, SYSUTCDATETIME(), NULL
                    FROM LandingFeatures;
                ');
                """);

            // 3. Re-key the heading settings under their language (the old copy was English).
            migrationBuilder.Sql(
                """
                UPDATE SiteSettings SET [Key] = [Key] + '.en'
                WHERE [Key] IN ('landing.features.eyebrow', 'landing.features.title');
                """);

            // 4. The old flat columns are now redundant.
            migrationBuilder.DropColumn(name: "Body", table: "LandingFeatures");
            migrationBuilder.DropColumn(name: "Title", table: "LandingFeatures");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Body",
                table: "LandingFeatures",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "LandingFeatures",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            // Fold the English translation back into the flat columns.
            migrationBuilder.Sql(
                """
                UPDATE lf SET lf.Title = t.Title, lf.Body = t.Body
                FROM LandingFeatures lf
                INNER JOIN LandingFeatureTranslations t
                    ON t.LandingFeatureId = lf.Id AND t.LanguageCode = 'en';
                """);

            // Drop the non-English heading settings and restore the English keys to their flat form.
            migrationBuilder.Sql(
                """
                DELETE FROM SiteSettings
                WHERE ([Key] LIKE 'landing.features.eyebrow.%' OR [Key] LIKE 'landing.features.title.%')
                  AND [Key] NOT IN ('landing.features.eyebrow.en', 'landing.features.title.en');
                UPDATE SiteSettings SET [Key] = LEFT([Key], LEN([Key]) - 3)
                WHERE [Key] IN ('landing.features.eyebrow.en', 'landing.features.title.en');
                """);

            migrationBuilder.DropTable(name: "LandingFeatureTranslations");
        }
    }
}
