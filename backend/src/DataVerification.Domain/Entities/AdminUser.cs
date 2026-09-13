using DataVerification.Domain.Common;

namespace DataVerification.Domain.Entities;

/// <summary>
/// A back-office operator. Authenticates with username <em>or</em> email plus a password;
/// authorized by direct permissions.
/// </summary>
public class AdminUser : Entity
{
    public required string Email { get; set; }

    /// <summary>
    /// The sign-in handle, stored lower-cased and unique. Cannot contain '@', so an identifier
    /// typed at the login form is unambiguously either a username or an email address.
    /// </summary>
    public required string Username { get; set; }

    public required string PasswordHash { get; set; }

    public required string FullName { get; set; }

    public bool IsActive { get; set; } = true;

    public string LanguageCode { get; set; } = "en";

    public int FailedLoginAttempts { get; set; }

    public DateTime? LockoutEndsAtUtc { get; set; }

    public DateTime? LastLoginAtUtc { get; set; }

    /// <summary>Provider-relative path of the profile photo, or null for the initials fallback.</summary>
    public string? AvatarStoragePath { get; set; }

    /// <summary>
    /// A hash of the outstanding password-reset token. The raw token is emailed and never stored,
    /// so a database read cannot be turned into a working reset link.
    /// </summary>
    public string? PasswordResetTokenHash { get; set; }

    public DateTime? PasswordResetTokenExpiresAtUtc { get; set; }

    public ICollection<AdminUserRole> UserRoles { get; set; } = [];

    public ICollection<AdminUserPermission> UserPermissions { get; set; } = [];

    public bool IsLockedOut(DateTime utcNow) => LockoutEndsAtUtc.HasValue && LockoutEndsAtUtc > utcNow;

    /// <summary>Flattens the user's direct grants into the distinct permission set placed on their JWT.</summary>
    public IReadOnlySet<string> ResolvePermissions() => UserPermissions
        .Select(up => up.Permission?.Name)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name!)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public void RegisterSuccessfulLogin(DateTime utcNow)
    {
        FailedLoginAttempts = 0;
        LockoutEndsAtUtc = null;
        LastLoginAtUtc = utcNow;
    }

    public void RegisterFailedLogin(DateTime utcNow, int maxAttempts, TimeSpan lockoutDuration)
    {
        FailedLoginAttempts++;
        if (FailedLoginAttempts >= maxAttempts)
        {
            LockoutEndsAtUtc = utcNow.Add(lockoutDuration);
            FailedLoginAttempts = 0;
        }
    }

    /// <summary>
    /// Replaces the password (already hashed by the caller) and revokes any outstanding reset
    /// token, so a link that was in flight cannot be used after the password has changed.
    /// </summary>
    public void SetPassword(string newPasswordHash, DateTime utcNow)
    {
        PasswordHash = newPasswordHash;
        UpdatedAtUtc = utcNow;
        ClearPasswordResetToken();
    }

    /// <summary>Stores the hash of a freshly issued reset token and its expiry.</summary>
    public void BeginPasswordReset(string tokenHash, DateTime expiresAtUtc)
    {
        PasswordResetTokenHash = tokenHash;
        PasswordResetTokenExpiresAtUtc = expiresAtUtc;
    }

    /// <summary>True while a reset token is outstanding and has not yet expired.</summary>
    public bool HasValidPasswordResetToken(DateTime utcNow) =>
        PasswordResetTokenHash is not null
        && PasswordResetTokenExpiresAtUtc.HasValue
        && PasswordResetTokenExpiresAtUtc.Value > utcNow;

    public void ClearPasswordResetToken()
    {
        PasswordResetTokenHash = null;
        PasswordResetTokenExpiresAtUtc = null;
    }

    public void SetAvatar(string storagePath, DateTime utcNow)
    {
        AvatarStoragePath = storagePath;
        UpdatedAtUtc = utcNow;
    }
}

/// <summary>A named bundle of permissions, e.g. SuperAdmin or Reviewer (templates only).</summary>
public class Role : Entity
{
    public required string Name { get; set; }

    public string? Description { get; set; }

    /// <summary>System roles are seeded and cannot be deleted through the admin UI.</summary>
    public bool IsSystemRole { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = [];

    public ICollection<AdminUserRole> UserRoles { get; set; } = [];
}

/// <summary>A single capability, checked by an authorization policy of the same name.</summary>
public class Permission : Entity
{
    public required string Name { get; set; }

    /// <summary>UI grouping, e.g. "Lookups" or "Applications".</summary>
    public required string Group { get; set; }

    public string? Description { get; set; }

    public ICollection<RolePermission> RolePermissions { get; set; } = [];

    public ICollection<AdminUserPermission> UserPermissions { get; set; } = [];
}

public class RolePermission : Entity
{
    public Guid RoleId { get; set; }

    public Role? Role { get; set; }

    public Guid PermissionId { get; set; }

    public Permission? Permission { get; set; }
}

public class AdminUserRole : Entity
{
    public Guid AdminUserId { get; set; }

    public AdminUser? AdminUser { get; set; }

    public Guid RoleId { get; set; }

    public Role? Role { get; set; }
}

/// <summary>Direct permission grant on an admin user — the source of truth for authorization.</summary>
public class AdminUserPermission : Entity
{
    public Guid AdminUserId { get; set; }

    public AdminUser? AdminUser { get; set; }

    public Guid PermissionId { get; set; }

    public Permission? Permission { get; set; }
}
