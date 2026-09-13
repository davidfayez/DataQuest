using DataVerification.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DataVerification.Infrastructure.Persistence.Configurations;

public sealed class AdminUserConfiguration : EntityConfigurationBase<AdminUser>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AdminUser> builder)
    {
        builder.ToTable("AdminUsers");

        builder.Property(u => u.Email).IsRequired().HasMaxLength(320);
        builder.Property(u => u.Username).IsRequired().HasMaxLength(50);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(500);
        builder.Property(u => u.FullName).IsRequired().HasMaxLength(200);
        builder.Property(u => u.LanguageCode).IsRequired().HasMaxLength(10);
        builder.Property(u => u.AvatarStoragePath).HasMaxLength(400);
        builder.Property(u => u.PasswordResetTokenHash).HasMaxLength(200);

        builder.HasIndex(u => u.Email).IsUnique();
        builder.HasIndex(u => u.Username).IsUnique();
    }
}

public sealed class RoleConfiguration : EntityConfigurationBase<Role>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");

        builder.Property(r => r.Name).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Description).HasMaxLength(500);

        builder.HasIndex(r => r.Name).IsUnique();
    }
}

public sealed class PermissionConfiguration : EntityConfigurationBase<Permission>
{
    protected override void ConfigureEntity(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");

        builder.Property(p => p.Name).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Group).IsRequired().HasMaxLength(100);
        builder.Property(p => p.Description).HasMaxLength(500);

        builder.HasIndex(p => p.Name).IsUnique();
    }
}

public sealed class RolePermissionConfiguration : EntityConfigurationBase<RolePermission>
{
    protected override void ConfigureEntity(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");

        builder.HasOne(rp => rp.Role)
            .WithMany(r => r.RolePermissions)
            .HasForeignKey(rp => rp.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(rp => rp.Permission)
            .WithMany(p => p.RolePermissions)
            .HasForeignKey(rp => rp.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(rp => new { rp.RoleId, rp.PermissionId }).IsUnique();
    }
}

public sealed class AdminUserRoleConfiguration : EntityConfigurationBase<AdminUserRole>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AdminUserRole> builder)
    {
        builder.ToTable("AdminUserRoles");

        builder.HasOne(ur => ur.AdminUser)
            .WithMany(u => u.UserRoles)
            .HasForeignKey(ur => ur.AdminUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(ur => ur.Role)
            .WithMany(r => r.UserRoles)
            .HasForeignKey(ur => ur.RoleId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(ur => new { ur.AdminUserId, ur.RoleId }).IsUnique();
    }
}

public sealed class AdminUserPermissionConfiguration : EntityConfigurationBase<AdminUserPermission>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AdminUserPermission> builder)
    {
        builder.ToTable("AdminUserPermissions");

        builder.HasOne(up => up.AdminUser)
            .WithMany(u => u.UserPermissions)
            .HasForeignKey(up => up.AdminUserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(up => up.Permission)
            .WithMany(p => p.UserPermissions)
            .HasForeignKey(up => up.PermissionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(up => new { up.AdminUserId, up.PermissionId }).IsUnique();
    }
}

public sealed class AuditLogEntryConfiguration : EntityConfigurationBase<AuditLogEntry>
{
    protected override void ConfigureEntity(EntityTypeBuilder<AuditLogEntry> builder)
    {
        builder.ToTable("AuditLog");

        builder.Property(a => a.Action).IsRequired().HasMaxLength(100);
        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.ActorType).HasConversion<int>();
        builder.Property(a => a.ActorName).HasMaxLength(200);
        builder.Property(a => a.Data).HasColumnType("nvarchar(max)");
        builder.Property(a => a.IpAddress).HasMaxLength(64);

        // The audit viewer filters by entity, by actor, and always sorts newest first.
        builder.HasIndex(a => new { a.EntityType, a.EntityId });
        builder.HasIndex(a => a.CreatedAtUtc);
        builder.HasIndex(a => new { a.ActorType, a.ActorId });
    }
}
