using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceTypeOutputLanguages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceTypeLanguages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    LanguageCode = table.Column<string>(type: "nvarchar(10)", maxLength: 10, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceTypeLanguages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceTypeLanguages_ServiceTypes_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalTable: "ServiceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypeLanguages_ServiceTypeId_LanguageCode",
                table: "ServiceTypeLanguages",
                columns: new[] { "ServiceTypeId", "LanguageCode" },
                unique: true);

            // Existing services keep every platform language, so the wizard offers exactly what it
            // did before this table existed. Narrowing a service is then a deliberate admin edit
            // rather than something the upgrade silently did on their behalf.
            migrationBuilder.Sql(
                """
                INSERT INTO [ServiceTypeLanguages] ([Id], [ServiceTypeId], [LanguageCode], [CreatedAtUtc])
                SELECT NEWID(), s.[Id], l.[Code], SYSUTCDATETIME()
                FROM [ServiceTypes] s
                CROSS JOIN (VALUES
                    ('ar'), ('en'), ('ru'), ('tr'), ('uz'),
                    ('de'), ('hi'), ('zh'), ('ja'), ('pl')
                ) AS l([Code]);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceTypeLanguages");
        }
    }
}
