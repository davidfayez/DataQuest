$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5088/api/v1'
$egypt = '22222222-0000-0000-0000-000000000001'
$egp = '22222222-0000-0000-0000-000000000002'
$txEducational = '44444444-0000-0000-0000-000000000001'
$txSecurity = '44444444-0000-0000-0000-000000000003'
$subBachelor = '55555555-0000-0000-0000-000000000001'
$subCriminal = '55555555-0000-0000-0000-000000000005'
$authority = '66666666-0000-0000-0000-000000000001'
$pass = 0; $fail = 0

function Check($name, $condition, $detail) {
  if ($condition) { Write-Output "  PASS  $name"; $script:pass++ }
  else { Write-Output "  FAIL  $name -- $detail"; $script:fail++ }
}

function Call($method, $path, $body, $token) {
  $headers = @{ 'Accept' = 'application/json' }
  if ($token) { $headers['Authorization'] = "Bearer $token" }
  $params = @{ Method = $method; Uri = "$base$path"; Headers = $headers; UseBasicParsing = $true; TimeoutSec = 20 }
  if ($body) {
    # PowerShell 5.1 mangles non-ASCII when handed a string body, so send explicit UTF-8 bytes.
    $params['Body'] = [System.Text.Encoding]::UTF8.GetBytes(($body | ConvertTo-Json -Depth 6))
    $params['ContentType'] = 'application/json; charset=utf-8'
  }
  try {
    $r = Invoke-WebRequest @params
    return @{ Status = [int]$r.StatusCode; Body = ($r.Content | ConvertFrom-Json) }
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

# A unique code/name per run keeps this suite repeatable against a database that already
# contains rows from a previous execution. Two letters is only 676 combinations, so the code is
# checked against the ones already stored rather than trusted to be free.
$letters = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ'.ToCharArray()
$existingCodes = @{}
$page = 1
do {
  $existing = Call GET "/admin/lookups/countries?page=$page&pageSize=200" $null $adminToken
  # A page with no items yields a single $null once wrapped, so the code is checked before use.
  foreach ($row in @($existing.Body.items)) {
    if ($row -and $row.code) { $existingCodes[$row.code] = $true }
  }
  $page++
} while ($existing.Body.hasNext -and $page -le 20)

$newCode = $null
foreach ($attempt in 1..500) {
  $candidate = -join (1..2 | ForEach-Object { $letters | Get-Random })
  if (-not $existingCodes.ContainsKey($candidate)) { $newCode = $candidate; break }
}
if (-not $newCode) { Write-Output '  FAIL  could not find an unused country code'; exit 1 }
$newNameEn = "Testland $newCode"
$newNameAr = "بلد $newCode"

# --- applicant with an Egypt order -------------------------------------------
$email = "cascade$([guid]::NewGuid().ToString('N').Substring(0,8))@example.com"
$reg = Call POST '/orders/register' @{ email = $email; languageCode = 'en' } $null
$login = Call POST '/orders/login' @{ orderNumber = $reg.Body.orderNumber; password = $reg.Body.password } $null
$token = $login.Body.accessToken
$null = Call PUT '/orders/setup' @{ verificationCountryId = $egypt; currencyId = $egp } $token

Write-Output "=== cascade level 1: transaction types scoped to order country ==="
$tx = Call GET '/transaction-types' $null $token
Check 'returns Egypt transaction types' ($tx.Status -eq 200 -and $tx.Body.Count -eq 3) "status $($tx.Status), count $($tx.Body.Count)"
Check 'resolved Name present' ($tx.Body[0].name -and $tx.Body[0].nameAr -and $tx.Body[0].nameEn) 'missing name fields'

Write-Output "=== cascade level 2: sub-types ==="
$subs = Call GET "/transaction-types/$txEducational/sub-types" $null $token
Check 'educational has 3 sub-types' ($subs.Status -eq 200 -and $subs.Body.Count -eq 3) "count $($subs.Body.Count)"
$secSubs = Call GET "/transaction-types/$txSecurity/sub-types" $null $token
Check 'security has 2 sub-types' ($secSubs.Body.Count -eq 2) "count $($secSubs.Body.Count)"

Write-Output "=== cascade level 3: authority mapping ==="
function CountOf($body) { if ($null -eq $body) { return 0 } return @($body).Count }
$auth = Call GET "/sub-types/$subBachelor/authorities" $null $token
Check 'bachelor maps to the seeded authority' ($auth.Status -eq 200 -and (CountOf $auth.Body) -eq 1) "status $($auth.Status) count $(CountOf $auth.Body) raw $($auth.Raw)"
$unmapped = Call GET "/sub-types/$subCriminal/authorities" $null $token
Check 'unmapped sub-type returns no authority' ($unmapped.Status -eq 200 -and (CountOf $unmapped.Body) -eq 0) "status $($unmapped.Status) count $(CountOf $unmapped.Body) raw $($unmapped.Raw)"

Write-Output "=== cascade level 4: service types and required files ==="
$svc = Call GET "/authorities/$authority/service-types" $null $token
Check 'authority offers 2 services' ($svc.Status -eq 200 -and $svc.Body.Count -eq 2) "count $($svc.Body.Count)"
$express = $svc.Body | Where-Object { $_.enableExpress }
$standard = $svc.Body | Where-Object { -not $_.enableExpress }
Check 'exactly one service enables express' (@($express).Count -eq 1) "got $(@($express).Count)"
Check 'express service advertises its surcharge' (@($express)[0].expressCost -gt 0) "got $(@($express)[0].expressCost)"
Check 'non-express service reports zero express cost' (@($standard)[0].expressCost -eq 0) "got $(@($standard)[0].expressCost)"
Check 'each service carries 2 required files' (($svc.Body | ForEach-Object { $_.requiredFiles.Count }) -eq @(2,2) -or ($svc.Body[0].requiredFiles.Count -eq 2 -and $svc.Body[1].requiredFiles.Count -eq 2)) 'required files missing'
Check 'services expose cost and execution time' ($svc.Body[0].cost -gt 0 -and $svc.Body[0].executionTimeDays -gt 0) 'pricing fields missing'

Write-Output "=== tampered ids are rejected, not silently empty ==="
$foreign = Call GET "/transaction-types/$([guid]::NewGuid())/sub-types" $null $token
Check 'unknown parent returns 404' ($foreign.Status -eq 404) "got $($foreign.Status)"

Write-Output "=== admin CRUD + country scoping ==="
$admin = Call POST '/admin/auth/login' @{ email = 'admin@dataverification.local'; password = 'Admin#12345' } $null
$adminToken = $admin.Body.accessToken

$newCountry = Call POST '/admin/lookups/countries' @{ code = $newCode; phoneCode = '+99'; nameAr = $newNameAr; nameEn = $newNameEn; isActive = $true } $adminToken
Check 'admin creates a country' ($newCountry.Status -eq 200 -and $newCountry.Body.code -eq $newCode) "status $($newCountry.Status) $($newCountry.Raw)"
$jordanId = $newCountry.Body.id

$dupe = Call POST '/admin/lookups/countries' @{ code = $newCode; phoneCode = '+99'; nameAr = 'x'; nameEn = 'y'; isActive = $true } $adminToken
Check 'duplicate country code rejected with 409' ($dupe.Status -eq 409) "got $($dupe.Status)"
Check 'duplicate carries a machine code' ($dupe.Body.code -eq 'country.duplicate_code') "got '$($dupe.Body.code)'"

$jordanTx = Call POST '/admin/lookups/transaction-types' @{ countryIds = @($jordanId); nameAr = "معاملة $newCode"; nameEn = "Transaction $newCode"; isActive = $true } $adminToken
Check 'admin creates a transaction type in the new country' ($jordanTx.Status -eq 200) "status $($jordanTx.Status) $($jordanTx.Raw)"

$txAfter = Call GET '/transaction-types' $null $token
Check 'Egypt applicant still sees only Egypt transaction types' ($txAfter.Body.Count -eq 3) "count $($txAfter.Body.Count)"

Write-Output "=== admin list paging and search ==="
$paged = Call GET '/admin/lookups/countries?page=1&pageSize=1' $null $adminToken
Check 'paging honoured' (@($paged.Body.items).Count -eq 1 -and $paged.Body.totalCount -ge 2) "items $(@($paged.Body.items).Count), total $($paged.Body.totalCount)"
$searchEn = Call GET "/admin/lookups/countries?search=$newNameEn" $null $adminToken
Check 'search matches the English name' (@($searchEn.Body.items).Count -eq 1) "count $(@($searchEn.Body.items).Count)"
$searchAr = Call GET "/admin/lookups/countries?search=$([uri]::EscapeDataString($newNameAr))" $null $adminToken
Check 'search matches the Arabic name' (@($searchAr.Body.items).Count -eq 1) "count $(@($searchAr.Body.items).Count)"

Write-Output "=== referential safety ==="
$delEgypt = Call DELETE "/admin/lookups/countries/$egypt" $null $adminToken
Check 'country in use is deactivated, not deleted' ($delEgypt.Status -eq 200 -and $delEgypt.Body -eq 1) "status $($delEgypt.Status), outcome $($delEgypt.Body)"
# Restoring Egypt is what lets every later suite run: leave it deactivated and order setup 404s,
# which cascades through applications, wallet and review. Assert the restore rather than discarding
# it, so a failure here is reported instead of silently corrupting the seed data.
$restoreEgypt = Call POST '/admin/lookups/countries' @{ id = $egypt; code = 'EG'; phoneCode = '+20'; nameAr = 'مصر'; nameEn = 'Egypt'; isActive = $true } $adminToken
Check 'Egypt restored after the deactivation check' ($restoreEgypt.Status -eq 200 -and $restoreEgypt.Body.isActive -eq $true) "status $($restoreEgypt.Status) $($restoreEgypt.Raw)"

Write-Output "=== express validation ==="
$badExpress = Call POST '/admin/lookups/service-types' @{
  verificationAuthorityId = $authority; nameAr = 'س'; nameEn = 'Bad express'
  executionTimeDays = 5; cost = 100; enableExpress = $true; expressCost = 0; isActive = $true
  requiredFiles = @(); outputLanguages = @('en')
} $adminToken
Check 'express enabled with zero surcharge rejected' ($badExpress.Status -eq 400) "got $($badExpress.Status)"

Write-Output "=== service type output languages ==="
$svcList = Call GET '/admin/lookups/service-types?page=1&pageSize=50' $null $adminToken
$seededService = @($svcList.Body.items) | Where-Object { $_.nameEn -eq 'Standard Certificate Verification' } | Select-Object -First 1
Check 'service types expose their output languages' (@($seededService.outputLanguages).Count -gt 0) "got $(@($seededService.outputLanguages).Count)"

function ServiceBody($languages) {
  return @{
    id = $seededService.id
    verificationAuthorityId = $seededService.verificationAuthorityId
    subTransactionTypeId = $seededService.subTransactionTypeId
    nameAr = $seededService.nameAr; nameEn = $seededService.nameEn
    descriptionAr = $seededService.descriptionAr; descriptionEn = $seededService.descriptionEn
    executionTimeDays = $seededService.executionTimeDays
    enableExpress = $seededService.enableExpress
    isActive = $seededService.isActive; showOnLanding = $seededService.showOnLanding
    costs = @(@($seededService.costs) | ForEach-Object { @{ currencyId = $_.currencyId; cost = $_.cost; expressCost = $_.expressCost } })
    requiredFiles = @(@($seededService.requiredFiles) | ForEach-Object {
      @{
        id = $_.id; nameAr = $_.nameAr; nameEn = $_.nameEn; isMandatory = $_.isMandatory
        maxSizeBytes = $_.maxSizeBytes; maxFiles = $_.maxFiles; fields = @()
      }
    })
    outputLanguages = $languages
  }
}

$noLanguages = Call POST '/admin/lookups/service-types' (ServiceBody @()) $adminToken
Check 'a service type with no output language is rejected' ($noLanguages.Status -eq 400) "got $($noLanguages.Status)"

$badLanguage = Call POST '/admin/lookups/service-types' (ServiceBody @('en', 'fr')) $adminToken
Check 'an unsupported output language is rejected' ($badLanguage.Status -eq 400) "got $($badLanguage.Status)"

$narrowed = Call POST '/admin/lookups/service-types' (ServiceBody @('en', 'ar')) $adminToken
Check 'output languages narrowed to the chosen set' (@($narrowed.Body.outputLanguages).Count -eq 2) "got $($narrowed.Body.outputLanguages -join ',')"

# The applicant cascade must serve exactly what the admin configured.
$applicantServices = Call GET "/authorities/$authority/service-types?subTransactionTypeId=$($seededService.subTransactionTypeId)" $null $token
$applicantService = @($applicantServices.Body) | Where-Object { $_.id -eq $seededService.id } | Select-Object -First 1
Check 'the applicant sees the configured output languages' (@($applicantService.outputLanguages).Count -eq 2) "got $($applicantService.outputLanguages -join ',')"

$restoreService = Call POST '/admin/lookups/service-types' (ServiceBody @('ar', 'en', 'ru', 'tr', 'uz', 'de', 'hi', 'zh', 'ja', 'pl')) $adminToken
Check 'output languages restored for later suites' (@($restoreService.Body.outputLanguages).Count -eq 10) "got $(@($restoreService.Body.outputLanguages).Count)"

Write-Output "=== required document limits and custom fields ==="
Check 'documents expose their upload limits' (@($seededService.requiredFiles)[0].maxFiles -ge 1 -and @($seededService.requiredFiles)[0].maxSizeBytes -gt 0) "got $(@($seededService.requiredFiles)[0].maxFiles)"

function DocumentBody($documents) {
  $body = ServiceBody @('ar', 'en', 'ru', 'tr', 'uz', 'de', 'hi', 'zh', 'ja', 'pl')
  $body.requiredFiles = $documents
  return $body
}

$firstDoc = @($seededService.requiredFiles)[0]
$otherDocs = @(@($seededService.requiredFiles) | Select-Object -Skip 1 | ForEach-Object {
  @{ id = $_.id; nameAr = $_.nameAr; nameEn = $_.nameEn; isMandatory = $_.isMandatory; maxSizeBytes = $_.maxSizeBytes; maxFiles = $_.maxFiles; fields = @() }
})

function FirstDocWith($maxFiles, $fields) {
  return , @{
    id = $firstDoc.id; nameAr = $firstDoc.nameAr; nameEn = $firstDoc.nameEn
    isMandatory = $firstDoc.isMandatory; maxSizeBytes = 524288; maxFiles = $maxFiles; fields = $fields
  } + $otherDocs
}

$zeroFiles = Call POST '/admin/lookups/service-types' (DocumentBody (FirstDocWith 0 @())) $adminToken
Check 'a document must accept at least one file' ($zeroFiles.Status -eq 400) "got $($zeroFiles.Status)"

$emptyDropdown = FirstDocWith 3 @(@{
  nameAr = 'نوع'; nameEn = 'Kind'; fieldType = 3; isRequired = $true; sortOrder = 0
  minLength = $null; maxLength = $null; pattern = $null; minValue = $null; maxValue = $null
  dateRule = 0; minDate = $null; maxDate = $null; options = @()
})
$noOptions = Call POST '/admin/lookups/service-types' (DocumentBody $emptyDropdown) $adminToken
Check 'a dropdown field must have options' ($noOptions.Status -eq 400) "got $($noOptions.Status)"

$configured = FirstDocWith 3 @(
  @{
    nameAr = 'تاريخ الإصدار'; nameEn = 'Issue date'; fieldType = 2; isRequired = $true; sortOrder = 0
    minLength = $null; maxLength = $null; pattern = $null; minValue = $null; maxValue = $null
    dateRule = 1; minDate = $null; maxDate = $null; options = @()
  },
  @{
    nameAr = 'رقم الإصدار'; nameEn = 'Issue number'; fieldType = 0; isRequired = $true; sortOrder = 1
    minLength = 4; maxLength = 12; pattern = '^[A-Z0-9-]+$'; minValue = $null; maxValue = $null
    dateRule = 0; minDate = $null; maxDate = $null; options = @()
  }
)
$withFields = Call POST '/admin/lookups/service-types' (DocumentBody $configured) $adminToken
$savedDoc = @($withFields.Body.requiredFiles) | Where-Object { $_.id -eq $firstDoc.id }
Check 'document limits and fields saved' ($withFields.Status -eq 200 -and $savedDoc.maxFiles -eq 3 -and $savedDoc.maxSizeBytes -eq 524288) "status $($withFields.Status) files $($savedDoc.maxFiles) size $($savedDoc.maxSizeBytes)"
Check 'both custom fields stored in order' (@($savedDoc.fields).Count -eq 2 -and @($savedDoc.fields)[0].nameEn -eq 'Issue date') "got $(@($savedDoc.fields).Count)"
Check 'text field keeps its validation rules' ((@($savedDoc.fields)[1].minLength -eq 4) -and (@($savedDoc.fields)[1].pattern -eq '^[A-Z0-9-]+$')) "got $(@($savedDoc.fields)[1].minLength)/$(@($savedDoc.fields)[1].pattern)"
Check 'date field keeps its past-only rule' (@($savedDoc.fields)[0].dateRule -eq 1) "got $(@($savedDoc.fields)[0].dateRule)"

# The applicant cascade must carry the same definitions the admin configured.
$applicantView = Call GET "/authorities/$authority/service-types?subTransactionTypeId=$($seededService.subTransactionTypeId)" $null $token
$applicantDoc = @(@($applicantView.Body) | Where-Object { $_.id -eq $seededService.id } | Select-Object -ExpandProperty requiredFiles) | Where-Object { $_.id -eq $firstDoc.id }
Check 'the applicant sees the document custom fields' (@($applicantDoc.fields).Count -eq 2) "got $(@($applicantDoc.fields).Count)"

$restoreDocs = Call POST '/admin/lookups/service-types' (DocumentBody (FirstDocWith 1 @())) $adminToken
$restoredDoc = @($restoreDocs.Body.requiredFiles) | Where-Object { $_.id -eq $firstDoc.id }
Check 'document fields cleared for later suites' (@($restoredDoc.fields).Count -eq 0) "got $(@($restoredDoc.fields).Count)"

Write-Output "=== RBAC (US5.1) ==="
$applicantOnAdmin = Call GET '/admin/lookups/countries' $null $token
Check 'applicant token refused on admin lookups' ($applicantOnAdmin.Status -eq 403) "got $($applicantOnAdmin.Status)"

$reviewer = Call POST '/admin/auth/login' @{ email = 'reviewer@dataverification.local'; password = 'Admin#12345' } $null
if ($reviewer.Status -eq 200) {
  $reviewerToken = $reviewer.Body.accessToken
  Check 'reviewer holds Countries.View' ($reviewer.Body.permissions -contains 'Countries.View') "perms: $($reviewer.Body.permissions -join ',')"
  Check 'reviewer lacks Countries.Create' (-not ($reviewer.Body.permissions -contains 'Countries.Create')) "perms: $($reviewer.Body.permissions -join ',')"
  $rView = Call GET '/admin/lookups/countries' $null $reviewerToken
  Check 'reviewer CAN read lookups (Countries.View)' ($rView.Status -eq 200) "got $($rView.Status)"
  $rWrite = Call POST '/admin/lookups/countries' @{ code = 'QA'; nameAr = 'قطر'; nameEn = 'Qatar'; isActive = $true } $reviewerToken
  Check 'reviewer CANNOT create lookups (403, lacks Countries.Create)' ($rWrite.Status -eq 403) "got $($rWrite.Status)"
} else {
  Write-Output "  SKIP  reviewer checks (no reviewer user seeded)"
}

Write-Output ""
Write-Output "PASSED: $pass   FAILED: $fail"
if ($fail -gt 0) { exit 1 }
