using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminUsername : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Added empty first, backfilled, and only then indexed. Creating the unique index
            // against the "" default would fail on any database holding more than one admin.
            migrationBuilder.AddColumn<string>(
                name: "Username",
                table: "AdminUsers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            // Derive a handle from the email local-part: lower-cased, stripped of everything the
            // username rules disallow, and short enough to leave room for a de-duplicating suffix.
            // Anything that reduces to fewer than three characters falls back to "admin", which
            // then de-duplicates like any other collision.
            // Wrapped in EXEC because it reads AdminUsers.Username, which this same migration
            // adds above. An idempotent script puts a whole migration in one batch, and SQL
            // Server compiles the batch before running it: new tables get deferred name
            // resolution, a new column on an existing table does not. Every single quote is
            // doubled because the statement now sits inside one.
            migrationBuilder.Sql(
                """
                EXEC(N'
                    WITH Sanitised AS (
                        SELECT
                            Id,
                            CreatedAtUtc,
                            Base = LEFT(
                                LOWER(
                                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                                    REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(REPLACE(
                                    REPLACE(REPLACE(REPLACE(REPLACE(
                                        SUBSTRING(Email, 1, CHARINDEX(''@'', Email + ''@'') - 1),
                                        ''+'', ''''), ''!'', ''''), ''#'', ''''), ''$'', ''''), ''%'', ''''), ''&'', ''''),
                                        ''*'', ''''), ''/'', ''''), ''='', ''''), ''?'', ''''), ''^'', ''''), ''`'', ''''),
                                        ''{'', ''''), ''|'', ''''), ''}'', ''''), ''~'', ''''), '''''''', ''''), '' '', '''')),
                                45)
                        FROM AdminUsers
                        WHERE Username = N''''
                    ),
                    Ranked AS (
                        SELECT
                            Id,
                            Base = CASE WHEN LEN(Base) < 3 THEN N''admin'' ELSE Base END,
                            CreatedAtUtc
                        FROM Sanitised
                    ),
                    Numbered AS (
                        SELECT
                            Id,
                            Base,
                            Ordinal = ROW_NUMBER() OVER (PARTITION BY Base ORDER BY CreatedAtUtc, Id)
                        FROM Ranked
                    )
                    UPDATE u
                    SET Username = CASE
                            WHEN n.Ordinal = 1 THEN n.Base
                            ELSE n.Base + N''.'' + CAST(n.Ordinal AS nvarchar(10))
                        END
                    FROM AdminUsers u
                    INNER JOIN Numbered n ON n.Id = u.Id;
                ');
                """);

            migrationBuilder.CreateIndex(
                name: "IX_AdminUsers_Username",
                table: "AdminUsers",
                column: "Username",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AdminUsers_Username",
                table: "AdminUsers");

            migrationBuilder.DropColumn(
                name: "Username",
                table: "AdminUsers");
        }
    }
}
