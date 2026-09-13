$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5088/api/v1'
$egypt = '22222222-0000-0000-0000-000000000001'
$egp = '22222222-0000-0000-0000-000000000002'
$pass = 0
$fail = 0

function Check($name, $condition, $detail) {
  if ($condition) { Write-Output "  PASS  $name"; $script:pass++ }
  else { Write-Output "  FAIL  $name -- $detail"; $script:fail++ }
}

function Call($method, $path, $body, $token) {
  $headers = @{ 'Accept' = 'application/json' }
  if ($token) { $headers['Authorization'] = "Bearer $token" }
  $params = @{ Method = $method; Uri = "$base$path"; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 20 }
  if ($body) { $params['Body'] = ($body | ConvertTo-Json -Depth 5); $params['ContentType'] = 'application/json' }
  try {
    $r = Invoke-WebRequest @params
    return @{ Status = [int]$r.StatusCode; Body = ($r.Content | ConvertFrom-Json) }
  } catch {
    $resp = $_.Exception.Response
    $status = if ($resp) { [int]$resp.StatusCode } else { 0 }
    $text = ''
    if ($resp) {
      $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
      $text = $reader.ReadToEnd()
    }
    $parsed = $null
    if ($text) { try { $parsed = $text | ConvertFrom-Json } catch { } }
    return @{ Status = $status; Body = $parsed; Raw = $text }
  }
}

Write-Output "=== US1.1 register ==="
$email = "applicant$([guid]::NewGuid().ToString('N').Substring(0,8))@example.com"
$reg = Call POST '/orders/register' @{ email = $email; languageCode = 'ar' } $null
Check 'register returns 200' ($reg.Status -eq 200) "got $($reg.Status)"
$orderNumber = $reg.Body.orderNumber
$password = $reg.Body.password
Check 'order number is the client code plus 9 digits' ($orderNumber -cmatch '^NEN[0-9]{9}$') "got '$orderNumber'"
Check 'password is 8 chars' ($password -and $password.Length -eq 8) "got '$password'"
Check 'password has upper, lower and digit' (($password -cmatch '[A-Z]') -and ($password -cmatch '[a-z]') -and ($password -match '[0-9]')) "got '$password'"

Write-Output "=== US1.1 validation ==="
$badEmail = Call POST '/orders/register' @{ email = 'not-an-email'; languageCode = 'en' } $null
Check 'invalid email rejected with 400' ($badEmail.Status -eq 400) "got $($badEmail.Status)"
Check 'validation problem carries code' ($badEmail.Body.code -eq 'request.validation_failed') "got '$($badEmail.Body.code)'"
$badLang = Call POST '/orders/register' @{ email = 'x@example.com'; languageCode = 'fr' } $null
Check 'unsupported language rejected' ($badLang.Status -eq 400) "got $($badLang.Status)"

Write-Output "=== US1.2 login ==="
$badLogin = Call POST '/orders/login' @{ orderNumber = $orderNumber; password = 'WrongPas1' } $null
Check 'wrong password returns 401' ($badLogin.Status -eq 401) "got $($badLogin.Status)"
Check 'wrong password gives generic message' ($badLogin.Body.detail -eq 'The credentials provided are not valid.') "got '$($badLogin.Body.detail)'"
$unknown = Call POST '/orders/login' @{ orderNumber = 'ZZZZZZZZZZZZ'; password = 'WrongPas1' } $null
Check 'unknown order gives identical response to wrong password' ($unknown.Status -eq 401 -and $unknown.Body.detail -eq $badLogin.Body.detail) "got $($unknown.Status) '$($unknown.Body.detail)'"

$login = Call POST '/orders/login' @{ orderNumber = $orderNumber; password = $password } $null
Check 'valid credentials return 200' ($login.Status -eq 200) "got $($login.Status)"
$token = $login.Body.accessToken
Check 'JWT issued' ($token -and $token.Split('.').Count -eq 3) 'token missing or malformed'
Check 'setup reported incomplete' ($login.Body.isSetupComplete -eq $false) "got $($login.Body.isSetupComplete)"

Write-Output "=== US1.3 order setup ==="
$noAuth = Call PUT '/orders/setup' @{ verificationCountryId = $egypt; currencyId = $egp } $null
Check 'setup without token returns 401' ($noAuth.Status -eq 401) "got $($noAuth.Status)"

$setup = Call PUT '/orders/setup' @{ verificationCountryId = $egypt; currencyId = $egp } $token
Check 'setup succeeds' ($setup.Status -eq 200) "got $($setup.Status) $($setup.Raw)"
Check 'wallet opens at zero' ($setup.Body.walletBalance -eq 0) "got $($setup.Body.walletBalance)"
Check 'currency locked to EGP' ($setup.Body.currencyCode -eq 'EGP') "got $($setup.Body.currencyCode)"

$again = Call PUT '/orders/setup' @{ verificationCountryId = $egypt; currencyId = $egp } $token
Check 'second setup rejected with 409' ($again.Status -eq 409) "got $($again.Status)"
Check 'conflict carries domain code' ($again.Body.code -eq 'order.setup_already_completed') "got '$($again.Body.code)'"

Write-Output "=== admin auth + RBAC ==="
$adminBad = Call POST '/admin/auth/login' @{ email = 'admin@dataverification.local'; password = 'wrong' } $null
Check 'admin wrong password returns 401' ($adminBad.Status -eq 401) "got $($adminBad.Status)"

$admin = Call POST '/admin/auth/login' @{ email = 'admin@dataverification.local'; password = 'Admin#12345' } $null
Check 'admin login succeeds' ($admin.Status -eq 200) "got $($admin.Status)"
Check 'SuperAdmin holds all 48 permissions' ($admin.Body.permissions.Count -eq 48) "got $($admin.Body.permissions.Count)"
Check 'SuperAdmin role assigned' ($admin.Body.roles -contains 'SuperAdmin') "got $($admin.Body.roles -join ',')"

Write-Output "=== cross-realm isolation ==="
$adminToken = $admin.Body.accessToken
$crossRealm = Call PUT '/orders/setup' @{ verificationCountryId = $egypt; currencyId = $egp } $adminToken
Check 'admin token rejected on applicant endpoint (403)' ($crossRealm.Status -eq 403) "got $($crossRealm.Status)"

Write-Output ""
Write-Output "PASSED: $pass   FAILED: $fail"
if ($fail -gt 0) { exit 1 }
