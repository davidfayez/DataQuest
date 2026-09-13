using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DataVerification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminUserPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminUserPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AdminUserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PermissionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminUserPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminUserPermissions_AdminUsers_AdminUserId",
                        column: x => x.AdminUserId,
                        principalTable: "AdminUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AdminUserPermissions_Permissions_PermissionId",
                        column: x => x.PermissionId,
                        principalTable: "Permissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminUserPermissions_AdminUserId_PermissionId",
                table: "AdminUserPermissions",
                columns: new[] { "AdminUserId", "PermissionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdminUserPermissions_PermissionId",
                table: "AdminUserPermissions",
                column: "PermissionId");

            // Preserve existing access: copy every role grant onto the user as a direct permission.
            migrationBuilder.Sql("""
                INSERT INTO AdminUserPermissions (Id, AdminUserId, PermissionId, CreatedAtUtc, UpdatedAtUtc)
                SELECT NEWID(), ur.AdminUserId, rp.PermissionId, SYSUTCDATETIME(), NULL
                FROM AdminUserRoles ur
                INNER JOIN RolePermissions rp ON rp.RoleId = ur.RoleId
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM AdminUserPermissions existing
                    WHERE existing.AdminUserId = ur.AdminUserId
                      AND existing.PermissionId = rp.PermissionId
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminUserPermissions");
        }
    }
}
