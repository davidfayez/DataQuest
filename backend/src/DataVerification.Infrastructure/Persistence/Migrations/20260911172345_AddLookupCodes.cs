using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLookupCodes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Added nullable, filled, then made required. EF's generated version added the column as
            // required with a blank default and created the unique index straight away, which fails
            // on any database that already has two rows: both would hold the same empty code.
            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "VerificationAuthorities",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "TransactionTypes",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "SubTransactionTypes",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "ServiceTypes",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: true);

            // Every backfill below runs through EXEC because it reads the Code column this same
            // migration adds above. `dotnet ef migrations script` emits one batch per migration and
            // SQL Server compiles a batch before executing any of it: it defers name resolution for
            // tables created in the batch, but not for columns added to an existing table. Without
            // EXEC, a script run against an empty database stops here with "Invalid column name
            // 'Code'". Single quotes are doubled because the statement now lives inside one.
            migrationBuilder.Sql(@"
                EXEC(N'
                    UPDATE target SET Code = seeded.Code
                    FROM [VerificationAuthorities] AS target
                    JOIN (VALUES
                        (CAST(''66666666-0000-0000-0000-000000000001'' AS uniqueidentifier), N''EG-SCU'')
                    ) AS seeded (Id, Code) ON seeded.Id = target.Id;

                    WITH numbered AS (
                        SELECT Id, ROW_NUMBER() OVER (ORDER BY CreatedAtUtc, Id) AS RowNumber
                        FROM [VerificationAuthorities]
                        WHERE Code IS NULL
                    )
                    UPDATE target SET Code = CONCAT(N''VA-'', REPLICATE(N''0'', 3 - LEN(numbered.RowNumber)), numbered.RowNumber)
                    FROM [VerificationAuthorities] AS target
                    JOIN numbered ON numbered.Id = target.Id;
                ');");

            migrationBuilder.Sql(@"
                EXEC(N'
                    UPDATE target SET Code = seeded.Code
                    FROM [TransactionTypes] AS target
                    JOIN (VALUES
                        (CAST(''44444444-0000-0000-0000-000000000001'' AS uniqueidentifier), N''EDU''),
                        (CAST(''44444444-0000-0000-0000-000000000002'' AS uniqueidentifier), N''PRO''),
                        (CAST(''44444444-0000-0000-0000-000000000003'' AS uniqueidentifier), N''SEC'')
                    ) AS seeded (Id, Code) ON seeded.Id = target.Id;

                    WITH numbered AS (
                        SELECT Id, ROW_NUMBER() OVER (ORDER BY CreatedAtUtc, Id) AS RowNumber
                        FROM [TransactionTypes]
                        WHERE Code IS NULL
                    )
                    UPDATE target SET Code = CONCAT(N''TT-'', REPLICATE(N''0'', 3 - LEN(numbered.RowNumber)), numbered.RowNumber)
                    FROM [TransactionTypes] AS target
                    JOIN numbered ON numbered.Id = target.Id;
                ');");

            migrationBuilder.Sql(@"
                EXEC(N'
                    UPDATE target SET Code = seeded.Code
                    FROM [SubTransactionTypes] AS target
                    JOIN (VALUES
                        (CAST(''55555555-0000-0000-0000-000000000001'' AS uniqueidentifier), N''EDU-BA''),
                        (CAST(''55555555-0000-0000-0000-000000000002'' AS uniqueidentifier), N''EDU-MA''),
                        (CAST(''55555555-0000-0000-0000-000000000003'' AS uniqueidentifier), N''EDU-PHD''),
                        (CAST(''55555555-0000-0000-0000-000000000004'' AS uniqueidentifier), N''PRO-EXP''),
                        (CAST(''55555555-0000-0000-0000-000000000005'' AS uniqueidentifier), N''SEC-CRIM''),
                        (CAST(''55555555-0000-0000-0000-000000000006'' AS uniqueidentifier), N''SEC-MIL'')
                    ) AS seeded (Id, Code) ON seeded.Id = target.Id;

                    WITH numbered AS (
                        SELECT Id, ROW_NUMBER() OVER (ORDER BY CreatedAtUtc, Id) AS RowNumber
                        FROM [SubTransactionTypes]
                        WHERE Code IS NULL
                    )
                    UPDATE target SET Code = CONCAT(N''STT-'', REPLICATE(N''0'', 3 - LEN(numbered.RowNumber)), numbered.RowNumber)
                    FROM [SubTransactionTypes] AS target
                    JOIN numbered ON numbered.Id = target.Id;
                ');");

            migrationBuilder.Sql(@"
                EXEC(N'
                    UPDATE target SET Code = seeded.Code
                    FROM [ServiceTypes] AS target
                    JOIN (VALUES
                        (CAST(''77777777-0000-0000-0000-000000000001'' AS uniqueidentifier), N''SVC-STD''),
                        (CAST(''77777777-0000-0000-0000-000000000002'' AS uniqueidentifier), N''SVC-ATT'')
                    ) AS seeded (Id, Code) ON seeded.Id = target.Id;

                    WITH numbered AS (
                        SELECT Id, ROW_NUMBER() OVER (ORDER BY CreatedAtUtc, Id) AS RowNumber
                        FROM [ServiceTypes]
                        WHERE Code IS NULL
                    )
                    UPDATE target SET Code = CONCAT(N''SVC-'', REPLICATE(N''0'', 3 - LEN(numbered.RowNumber)), numbered.RowNumber)
                    FROM [ServiceTypes] AS target
                    JOIN numbered ON numbered.Id = target.Id;
                ');");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "VerificationAuthorities",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "TransactionTypes",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "SubTransactionTypes",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "ServiceTypes",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(30)",
                oldMaxLength: 30,
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerificationAuthorities_Code",
                table: "VerificationAuthorities",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransactionTypes_Code",
                table: "TransactionTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SubTransactionTypes_Code",
                table: "SubTransactionTypes",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceTypes_Code",
                table: "ServiceTypes",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_VerificationAuthorities_Code",
                table: "VerificationAuthorities");

            migrationBuilder.DropIndex(
                name: "IX_TransactionTypes_Code",
                table: "TransactionTypes");

            migrationBuilder.DropIndex(
                name: "IX_SubTransactionTypes_Code",
                table: "SubTransactionTypes");

            migrationBuilder.DropIndex(
                name: "IX_ServiceTypes_Code",
                table: "ServiceTypes");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "VerificationAuthorities");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "TransactionTypes");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "SubTransactionTypes");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "ServiceTypes");
        }
    }
}
