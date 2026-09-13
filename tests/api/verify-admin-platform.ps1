$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5088/api/v1'
$pass = 0; $fail = 0

function Check($name, $condition, $detail) {
  if ($condition) { Write-Output "  PASS  $name"; $script:pass++ }
  else { Write-Output "  FAIL  $name -- $detail"; $script:fail++ }
}

function Call($method, $path, $body, $token) {
  $headers = @{ 'Accept' = 'application/json' }
  if ($token) { $headers['Authorization'] = "Bearer $token" }
  $params = @{ Method = $method; Uri = "$base$path"; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 30 }
  if ($body) {
    $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 8))
    $params['ContentType'] = 'application/json; charset=utf-8'
  }
  try {
    $r = Invoke-WebRequest @params
    return @{ Status = [int]$r.StatusCode; Body = ($r.Content | ConvertFrom-Json); Raw = $r.Content }
  } catch {
    $resp = $_.Exception.Response
    $status = if ($resp) { [int]$resp.StatusCode } else { 0 }
    $text = ''
    if ($resp) { $text = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd() }
    $parsed = $null
    if ($text) { try { $parsed = $text | ConvertFrom-Json } catch { } }
    return @{ Status = $status; Body = $parsed; Raw = $text }
  }
}

$admin = Call POST '/admin/auth/login' @{ email = 'admin@dataverification.local'; password = 'Admin#12345' } $null
$adminToken = $admin.Body.accessToken
$reviewer = Call POST '/admin/auth/login' @{ email = 'reviewer@dataverification.local'; password = 'Admin#12345' } $null
$reviewerToken = $reviewer.Body.accessToken

Write-Output "=== dashboard ==="
$dash = Call GET '/admin/dashboard' $null $adminToken
Check 'dashboard returns 200' ($dash.Status -eq 200) "status $($dash.Status) $($dash.Raw)"
Check 'every status is represented' (@($dash.Body.statusCounts).Count -eq 8) "got $(@($dash.Body.statusCounts).Count)"
Check 'totals are present' ($null -ne $dash.Body.totalApplications -and $null -ne $dash.Body.orderCount) 'missing counters'
Check 'revenue is derived from the ledger' ($dash.Body.revenueNet -eq ($dash.Body.revenueCollected - $dash.Body.revenueRefunded)) "net $($dash.Body.revenueNet)"
Check 'recent activity present' (@($dash.Body.recentActivity).Count -gt 0) "got $(@($dash.Body.recentActivity).Count)"

Write-Output "=== orders ==="
$orders = Call GET '/admin/orders?page=1&pageSize=5' $null $adminToken
Check 'order search returns a page' ($orders.Status -eq 200 -and @($orders.Body.items).Count -le 5) "status $($orders.Status)"
Check 'orders carry wallet and application counts' ($null -ne $orders.Body.items[0].walletBalance -and $null -ne $orders.Body.items[0].applicationCount) 'missing fields'

$firstOrderNumber = $orders.Body.items[0].orderNumber
$searched = Call GET "/admin/orders?search=$firstOrderNumber" $null $adminToken
Check 'search by order number narrows the result' ($searched.Body.totalCount -ge 1) "total $($searched.Body.totalCount)"

$orderId = $orders.Body.items[0].id
$detail = Call GET "/admin/orders/$orderId" $null $adminToken
Check 'order detail returns 200' ($detail.Status -eq 200) "status $($detail.Status)"
Check 'order detail lists applications' ($null -ne $detail.Body.applications) 'applications missing'
Check 'applications carry a refund capability flag' (@($detail.Body.applications).Count -eq 0 -or $null -ne $detail.Body.applications[0].canRefund) 'canRefund missing'

Write-Output "=== permissions and roles ==="
$perms = Call GET '/admin/permissions' $null $adminToken
Check 'permissions are grouped' ($perms.Status -eq 200 -and @($perms.Body).Count -ge 3) "groups $(@($perms.Body).Count)"
$flat = @($perms.Body) | ForEach-Object { $_.permissions } | ForEach-Object { $_ }
Check 'all 48 permissions returned' (@($flat).Count -eq 48) "got $(@($flat).Count)"

$roles = Call GET '/admin/roles' $null $adminToken
Check 'roles returned' ($roles.Status -eq 200 -and @($roles.Body).Count -ge 2) "count $(@($roles.Body).Count)"
$superAdmin = @($roles.Body) | Where-Object { $_.name -eq 'SuperAdmin' }
Check 'SuperAdmin holds every permission' (@($superAdmin.permissions).Count -eq 48) "got $(@($superAdmin.permissions).Count)"
Check 'system roles are flagged' ($superAdmin.isSystemRole -eq $true) "got $($superAdmin.isSystemRole)"

$roleName = "Auditor$([guid]::NewGuid().ToString('N').Substring(0,6))"
$newRole = Call POST '/admin/roles' @{ name = $roleName; description = 'Read-only auditor'; permissions = @('AuditLog.View', 'Applications.View') } $adminToken
Check 'role created' ($newRole.Status -eq 200 -and @($newRole.Body.permissions).Count -eq 2) "status $($newRole.Status) $($newRole.Raw)"

$dupe = Call POST '/admin/roles' @{ name = $roleName; description = 'x'; permissions = @() } $adminToken
Check 'duplicate role name rejected' ($dupe.Status -eq 409 -and $dupe.Body.code -eq 'role.duplicate_name') "status $($dupe.Status) code $($dupe.Body.code)"

$updated = Call POST '/admin/roles' @{ id = $newRole.Body.id; name = $roleName; description = 'Updated'; permissions = @('AuditLog.View') } $adminToken
Check 'role permissions replaced on update' (@($updated.Body.permissions).Count -eq 1) "got $(@($updated.Body.permissions).Count)"

$renameSystem = Call POST '/admin/roles' @{ id = $superAdmin.id; name = 'RenamedSuperAdmin'; permissions = @('AuditLog.View') } $adminToken
Check 'built-in role cannot be renamed' ($renameSystem.Status -eq 409 -and $renameSystem.Body.code -eq 'role.system_role_immutable_name') "status $($renameSystem.Status) code $($renameSystem.Body.code)"

$deleteSystem = Call DELETE "/admin/roles/$($superAdmin.id)" $null $adminToken
Check 'built-in role cannot be deleted' ($deleteSystem.Status -eq 409 -and $deleteSystem.Body.code -eq 'role.system_role_protected') "status $($deleteSystem.Status)"

$badPerm = Call POST '/admin/roles' @{ name = "Bogus$([guid]::NewGuid().ToString('N').Substring(0,6))"; permissions = @('Not.A.Real.Permission') } $adminToken
Check 'unknown permission rejected' ($badPerm.Status -eq 404) "status $($badPerm.Status)"

Write-Output "=== admin users ==="
$users = Call GET '/admin/users' $null $adminToken
Check 'admin users listed' ($users.Status -eq 200 -and $users.Body.totalCount -ge 1) "status $($users.Status)"

$newUsername = "operator$([guid]::NewGuid().ToString('N').Substring(0,8))"
$newEmail = "$newUsername@dataverification.local"
$newUser = Call POST '/admin/users' @{ email = $newEmail; username = $newUsername; fullName = 'Test Operator'; password = 'Operator#12345'; isActive = $true; languageCode = 'en'; permissions = @('AuditLog.View') } $adminToken
Check 'admin user created' ($newUser.Status -eq 200) "status $($newUser.Status) $($newUser.Raw)"
Check 'permission granted to the new user' (@($newUser.Body.permissions).Count -eq 1) "got $(@($newUser.Body.permissions).Count)"

$shortPassword = Call POST '/admin/users' @{ email = "x$([guid]::NewGuid().ToString('N').Substring(0,6))@d.local"; username = "x$([guid]::NewGuid().ToString('N').Substring(0,6))"; fullName = 'X'; password = 'short'; isActive = $true; languageCode = 'en'; permissions = @() } $adminToken
Check 'short password rejected' ($shortPassword.Status -eq 400) "status $($shortPassword.Status)"

$noPassword = Call POST '/admin/users' @{ email = "y$([guid]::NewGuid().ToString('N').Substring(0,6))@d.local"; username = "y$([guid]::NewGuid().ToString('N').Substring(0,6))"; fullName = 'Y'; isActive = $true; languageCode = 'en'; permissions = @() } $adminToken
Check 'password required when creating' ($noPassword.Status -eq 400) "status $($noPassword.Status)"

$noUsername = Call POST '/admin/users' @{ email = "z$([guid]::NewGuid().ToString('N').Substring(0,6))@d.local"; fullName = 'Z'; password = 'Operator#12345'; isActive = $true; languageCode = 'en'; permissions = @() } $adminToken
Check 'username required when creating' ($noUsername.Status -eq 400) "status $($noUsername.Status)"

$badUsername = Call POST '/admin/users' @{ email = "w$([guid]::NewGuid().ToString('N').Substring(0,6))@d.local"; username = 'has spaces!'; fullName = 'W'; password = 'Operator#12345'; isActive = $true; languageCode = 'en'; permissions = @() } $adminToken
Check 'malformed username rejected' ($badUsername.Status -eq 400) "status $($badUsername.Status)"

$dupeEmail = Call POST '/admin/users' @{ email = $newEmail; username = "dup$([guid]::NewGuid().ToString('N').Substring(0,6))"; fullName = 'Dup'; password = 'Operator#12345'; isActive = $true; languageCode = 'en'; permissions = @() } $adminToken
Check 'duplicate email rejected' ($dupeEmail.Status -eq 409 -and $dupeEmail.Body.code -eq 'admin_user.duplicate_email') "status $($dupeEmail.Status)"

$dupeUsername = Call POST '/admin/users' @{ email = "dup$([guid]::NewGuid().ToString('N').Substring(0,6))@d.local"; username = $newUsername; fullName = 'Dup'; password = 'Operator#12345'; isActive = $true; languageCode = 'en'; permissions = @() } $adminToken
Check 'duplicate username rejected' ($dupeUsername.Status -eq 409 -and $dupeUsername.Body.code -eq 'admin_user.duplicate_username') "status $($dupeUsername.Status)"

# The new operator can sign in with the assigned role's permissions.
$operatorLogin = Call POST '/admin/auth/login' @{ email = $newEmail; password = 'Operator#12345' } $null
Check 'new operator can sign in' ($operatorLogin.Status -eq 200) "status $($operatorLogin.Status)"
Check 'operator has exactly the role permissions' (@($operatorLogin.Body.permissions).Count -eq 1 -and $operatorLogin.Body.permissions[0] -eq 'AuditLog.View') "perms $($operatorLogin.Body.permissions -join ',')"

# The same account signs in by username, through the current field name.
$byUsername = Call POST '/admin/auth/login' @{ usernameOrEmail = $newUsername; password = 'Operator#12345' } $null
Check 'operator can sign in by username' ($byUsername.Status -eq 200) "status $($byUsername.Status)"
Check 'login returns the username' ($byUsername.Body.username -eq $newUsername) "got $($byUsername.Body.username)"

$deactivate = Call POST "/admin/users/$($newUser.Body.id)/active" @{ isActive = $false } $adminToken
Check 'admin user deactivated' ($deactivate.Status -eq 204) "status $($deactivate.Status)"
$blocked = Call POST '/admin/auth/login' @{ email = $newEmail; password = 'Operator#12345' } $null
Check 'deactivated user cannot sign in' ($blocked.Status -eq 401) "status $($blocked.Status)"

$selfOff = Call POST "/admin/users/$($admin.Body.adminUserId)/active" @{ isActive = $false } $adminToken
Check 'cannot deactivate your own account' ($selfOff.Status -eq 409 -and $selfOff.Body.code -eq 'admin_user.cannot_deactivate_self') "status $($selfOff.Status)"

Write-Output "=== audit log ==="
$audit = Call GET '/admin/audit-log?page=1&pageSize=10' $null $adminToken
Check 'audit log returns a page' ($audit.Status -eq 200 -and @($audit.Body.items).Count -gt 0) "status $($audit.Status)"
Check 'entries carry actor and timestamp' ($null -ne $audit.Body.items[0].actorTypeName -and $null -ne $audit.Body.items[0].createdAtUtc) 'missing actor/timestamp'
$filtered = Call GET '/admin/audit-log?action=Role' $null $adminToken
Check 'action filter works' (@($filtered.Body.items).Count -gt 0 -and $filtered.Body.items[0].action -like '*Role*') "first $($filtered.Body.items[0].action)"
$byEntity = Call GET '/admin/audit-log?entityType=AdminUser' $null $adminToken
Check 'entity type filter works' (@($byEntity.Body.items | Where-Object { $_.entityType -ne 'AdminUser' }).Count -eq 0) 'mixed entity types returned'
Check 'admin user creation was audited' (@($byEntity.Body.items | Where-Object { $_.action -eq 'AdminUser.Created' }).Count -ge 1) 'creation not audited'

Write-Output "=== RBAC on the new endpoints (US5.1) ==="
if ($reviewerToken) {
  Check 'reviewer can read the dashboard (Applications.View)' ((Call GET '/admin/dashboard' $null $reviewerToken).Status -eq 200) 'expected 200'
  Check 'reviewer cannot list roles (no Roles.View)' ((Call GET '/admin/roles' $null $reviewerToken).Status -eq 403) 'expected 403'
  Check 'reviewer cannot list admin users' ((Call GET '/admin/users' $null $reviewerToken).Status -eq 403) 'expected 403'
  Check 'reviewer cannot read the audit log' ((Call GET '/admin/audit-log' $null $reviewerToken).Status -eq 403) 'expected 403'
  Check 'reviewer can view orders (Orders.View)' ((Call GET '/admin/orders' $null $reviewerToken).Status -eq 200) 'expected 200'
}

$anon = Call GET '/admin/dashboard' $null $null
Check 'anonymous access refused' ($anon.Status -eq 401) "status $($anon.Status)"

Write-Output ""
Write-Output "PASSED: $pass   FAILED: $fail"
if ($fail -gt 0) { exit 1 }
