/*
    New permissions introduced by this release.

    Production does not run the seeder, so these catalogue rows have to be inserted by hand —
    without them nobody can be granted the new screens, and the API returns 403 for everyone.

    Safe to run more than once: every statement checks before it writes.

    >>> Set @GrantToEmail to the administrator who should receive the permissions. <<<
    Leave it NULL to create the catalogue rows without granting them to anyone yet (you can then
    assign them per user from Admin users in the panel).

    The grant only takes effect at the NEXT SIGN-IN: permissions are baked into the JWT at login.
*/

DECLARE @GrantToEmail nvarchar(320) = NULL;   -- e.g. N'david.fayez@watanfd.com'

-- ---------------------------------------------------------------- catalogue

DECLARE @New TABLE (Name nvarchar(200), [Group] nvarchar(200), Description nvarchar(500));

INSERT INTO @New (Name, [Group], Description) VALUES
    (N'Orders.ViewPassword', N'Orders',   N'Reveal an order''s sign-in password in clear text.'),
    (N'Settings.View',       N'Settings', N'Open the platform settings page.'),
    (N'Settings.Update',     N'Settings', N'Change platform settings, including the email API key.'),
    (N'Tools.View',          N'Tools',    N'View the tools and guides shown to applicants.'),
    (N'Tools.Create',        N'Tools',    N'Create the tools and guides shown to applicants.'),
    (N'Tools.Update',        N'Tools',    N'Edit the tools and guides shown to applicants.'),
    (N'Tools.Delete',        N'Tools',    N'Delete the tools and guides shown to applicants.');

INSERT INTO Permissions (Id, Name, [Group], Description, CreatedAtUtc)
SELECT NEWID(), n.Name, n.[Group], n.Description, GETUTCDATE()
FROM @New AS n
WHERE NOT EXISTS (SELECT 1 FROM Permissions AS p WHERE p.Name = n.Name);

-- Assigned first: T-SQL does not allow a subquery inside PRINT/CONCAT.
DECLARE @Present int = (SELECT COUNT(*) FROM Permissions WHERE Name IN (SELECT Name FROM @New));
PRINT CONCAT(N'Permission catalogue rows present: ', @Present, N' of 7');

-- ------------------------------------------------------------------- grant

IF @GrantToEmail IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM AdminUsers WHERE Email = @GrantToEmail)
        RAISERROR (N'No admin user with that email address - nothing was granted.', 16, 1);
    ELSE
    BEGIN
        INSERT INTO AdminUserPermissions (Id, AdminUserId, PermissionId, CreatedAtUtc)
        SELECT NEWID(), u.Id, p.Id, GETUTCDATE()
        FROM AdminUsers AS u
        CROSS JOIN Permissions AS p
        WHERE u.Email = @GrantToEmail
          AND p.Name IN (SELECT Name FROM @New)
          AND NOT EXISTS (
              SELECT 1 FROM AdminUserPermissions AS existing
              WHERE existing.AdminUserId = u.Id AND existing.PermissionId = p.Id);

        PRINT CONCAT(N'Granted to ', @GrantToEmail, N'. Sign out and back in for it to take effect.');
    END
END
ELSE
    PRINT N'@GrantToEmail was NULL - catalogue rows created, no grants made.';
