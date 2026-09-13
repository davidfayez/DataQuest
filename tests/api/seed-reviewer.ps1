# Adds a Reviewer admin so the RBAC checks can prove a permission-denied path.
# The password hash is copied from the seeded SuperAdmin, so both share "Admin#12345".
# Must point at the database the API under test is actually using, or the reviewer lands in a
# database nothing reads and every RBAC check fails on a 401. Override with DV_VERIFY_CONNECTION.
$connectionString = if ($env:DV_VERIFY_CONNECTION) { $env:DV_VERIFY_CONNECTION } else {
  'Server=localhost,1433;Database=DataVerification;User Id=sa;Password=Your_strong_Passw0rd;TrustServerCertificate=True;Encrypt=False'
}

$sql = @"
IF NOT EXISTS (SELECT 1 FROM AdminUsers WHERE Email = 'reviewer@dataverification.local')
BEGIN
    DECLARE @hash NVARCHAR(500) = (SELECT TOP 1 PasswordHash FROM AdminUsers WHERE Email = 'admin@dataverification.local');
    DECLARE @id UNIQUEIDENTIFIER = NEWID();
    DECLARE @reviewerRole UNIQUEIDENTIFIER = (SELECT TOP 1 Id FROM Roles WHERE Name = 'Reviewer');

    INSERT INTO AdminUsers (Id, Email, Username, PasswordHash, FullName, IsActive, LanguageCode, FailedLoginAttempts, CreatedAtUtc)
    VALUES (@id, 'reviewer@dataverification.local', 'reviewer', @hash, 'Seeded Reviewer', 1, 'en', 0, SYSUTCDATETIME());

    INSERT INTO AdminUserRoles (Id, AdminUserId, RoleId, CreatedAtUtc)
    VALUES (NEWID(), @id, @reviewerRole, SYSUTCDATETIME());
END
SELECT COUNT(*) FROM AdminUsers WHERE Email = 'reviewer@dataverification.local';
"@

$connection = New-Object System.Data.SqlClient.SqlConnection $connectionString
$connection.Open()
$command = $connection.CreateCommand()
$command.CommandText = $sql
Write-Output "reviewer users: $($command.ExecuteScalar())"
$connection.Close()
