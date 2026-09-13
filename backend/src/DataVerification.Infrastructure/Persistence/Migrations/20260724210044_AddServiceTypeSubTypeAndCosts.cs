using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceTypeSubTypeAndCosts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SubTransactionTypeId",
                table: "ServiceTypes",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ServiceTypeCosts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ServiceTypeId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CurrencyId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Cost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    ExpressCost = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceTypeCosts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceTypeCosts_Currencies_CurrencyId",
                        column: x => x.CurrencyId,
                        principalTable: "Currencies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceTypeCosts_ServiceTypes_ServiceTypeId",
                        column: x => x.ServiceTypeId,
                        principalTable: "ServiceTypes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Prefer a sub-type the authority already handles.
            //
            // Both backfills run through EXEC because they read ServiceTypes.SubTransactionTypeId,
            // which this same migration adds a few lines above. `dotnet ef migrations script`
            // emits one batch per migration and SQL Server compiles a batch before running any of
            // it — and while it defers name resolution for tables created in the batch, it does
            // not for columns added to an existing one. Without EXEC, a script run against an
            // empty database stops here with "Invalid column name 'SubTransactionTypeId'".
            migrationBuilder.Sql("""
                EXEC(N'
                    UPDATE st
                    SET st.SubTransactionTypeId = picked.SubTransactionTypeId
                    FROM ServiceTypes st
                    INNER JOIN (
                        SELECT VerificationAuthorityId, MIN(CONVERT(nvarchar(36), SubTransactionTypeId)) AS SubTypeText
                        FROM AuthoritySubTransactionTypes
                        GROUP BY VerificationAuthorityId
                    ) authorityPick ON authorityPick.VerificationAuthorityId = st.VerificationAuthorityId
                    INNER JOIN AuthoritySubTransactionTypes picked
                        ON picked.VerificationAuthorityId = authorityPick.VerificationAuthorityId
                        AND CONVERT(nvarchar(36), picked.SubTransactionTypeId) = authorityPick.SubTypeText
                    WHERE st.SubTransactionTypeId IS NULL;
                ');
                """);

            // Any remaining rows get an arbitrary active sub-type so the column can become required.
            migrationBuilder.Sql("""
                EXEC(N'
                    UPDATE st
                    SET st.SubTransactionTypeId = (
                        SELECT TOP 1 Id FROM SubTransactionTypes ORDER BY NameEn
                    )
                    FROM ServiceTypes st
                    WHERE st.SubTransactionTypeId IS NULL;
                ');
                """);

            // Seed one cost row per currency already mapped to the authority's country.
            migrationBuilder.Sql("""
                INSERT INTO ServiceTypeCosts (Id, ServiceTypeId, CurrencyId, Cost, ExpressCost, CreatedAtUtc, UpdatedAtUtc)
                SELECT NEWID(), st.Id, cc.CurrencyId, st.Cost, st.ExpressCost, SYSUTCDATETIME(), NULL
                FROM ServiceTypes st
                INNER JOIN VerificationAuthorities va ON va.Id = st.VerificationAuthorityId
                INNER JOIN CountryCurrencies cc ON cc.CountryId = va.CountryId;
                """);

            migrationBuilder.AlterColumn<Guid>(
                name: "SubTransactionTypeId",
                table: "ServiceTypes",
                type: "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypes_SubTransactionTypeId_VerificationAuthorityId_IsActive",
                table: "ServiceTypes",
                columns: new[] { "SubTransactionTypeId", "VerificationAuthorityId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypeCosts_CurrencyId",
                table: "ServiceTypeCosts",
                column: "CurrencyId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypeCosts_ServiceTypeId_CurrencyId",
                table: "ServiceTypeCosts",
                columns: new[] { "ServiceTypeId", "CurrencyId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ServiceTypes_SubTransactionTypes_SubTransactionTypeId",
                table: "ServiceTypes",
                column: "SubTransactionTypeId",
                principalTable: "SubTransactionTypes",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ServiceTypes_SubTransactionTypes_SubTransactionTypeId",
                table: "ServiceTypes");

            migrationBuilder.DropTable(
                name: "ServiceTypeCosts");

            migrationBuilder.DropIndex(
                name: "IX_ServiceTypes_SubTransactionTypeId_VerificationAuthorityId_IsActive",
                table: "ServiceTypes");

            migrationBuilder.DropColumn(
                name: "SubTransactionTypeId",
                table: "ServiceTypes");
        }
    }
}
