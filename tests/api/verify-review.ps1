$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5088/api/v1'
$egypt = '22222222-0000-0000-0000-000000000001'
$egp = '22222222-0000-0000-0000-000000000002'
$txEducational = '44444444-0000-0000-0000-000000000001'
$subBachelor = '55555555-0000-0000-0000-000000000001'
$authority = '66666666-0000-0000-0000-000000000001'
$svcStandard = '77777777-0000-0000-0000-000000000001'
$reqStdCert = '88888888-0000-0000-0000-000000000001'
$reqStdId = '88888888-0000-0000-0000-000000000002'
$pass = 0; $fail = 0

# A distinctive marker: if this string appears anywhere in an applicant-facing payload,
# the internal comment leaked.
$INTERNAL_SECRET = 'INTERNAL-ONLY-XYZZY-DO-NOT-LEAK-7734'
$FORUSER_TEXT = 'الصورة مش واضحة، ارفعها تاني'

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

function Upload($applicationId, $fileName, [byte[]]$bytes, $serviceId, $requiredFileId, $token, $path) {
  if (-not $path) { $path = "/applications/$applicationId/files" }
  $boundary = [guid]::NewGuid().ToString()
  $nl = "`r`n"
  $ms = New-Object System.IO.MemoryStream
  function Append([string]$s) { $b = [System.Text.Encoding]::UTF8.GetBytes($s); $ms.Write($b, 0, $b.Length) }
  if ($serviceId) {
    Append "--$boundary$nl"; Append "Content-Disposition: form-data; name=`"applicationServiceId`"$nl$nl$serviceId$nl"
  }
  if ($requiredFileId) {
    Append "--$boundary$nl"; Append "Content-Disposition: form-data; name=`"requiredFileId`"$nl$nl$requiredFileId$nl"
  }
  Append "--$boundary$nl"
  Append "Content-Disposition: form-data; name=`"file`"; filename=`"$fileName`"${nl}Content-Type: application/octet-stream$nl$nl"
  $ms.Write($bytes, 0, $bytes.Length)
  Append "$nl--$boundary--$nl"
  try {
    $r = Invoke-WebRequest -Method POST -Uri "$base$path" -Headers @{ 'Authorization' = "Bearer $token" } `
      -ContentType "multipart/form-data; boundary=$boundary" -Body $ms.ToArray() -UseBasicParsing -TimeoutSec 60
    return @{ Status = [int]$r.StatusCode; Body = ($r.Content | ConvertFrom-Json) }
  } catch {
    $resp = $_.Exception.Response
    $status = if ($resp) { [int]$resp.StatusCode } else { 0 }
    $text = ''
    if ($resp) { $text = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd() }
    return @{ Status = $status; Raw = $text }
  } finally { $ms.Dispose() }
}

$pdfBytes = [System.Text.Encoding]::ASCII.GetBytes("%PDF-1.4`n% doc`n") + [byte[]](1..32)
$pngBytes = [byte[]](0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A) + [byte[]](1..32)

$admin = Call POST '/admin/auth/login' @{ email = 'admin@dataverification.local'; password = 'Admin#12345' } $null
$adminToken = $admin.Body.accessToken

# Applicant with a paid application sitting in the queue.
$email = "review$([guid]::NewGuid().ToString('N').Substring(0,8))@example.com"
$reg = Call POST '/orders/register' @{ email = $email; languageCode = 'ar' } $null
$login = Call POST '/orders/login' @{ orderNumber = $reg.Body.orderNumber; password = $reg.Body.password } $null
$token = $login.Body.accessToken
$orderId = $login.Body.orderId
$null = Call PUT '/orders/setup' @{ verificationCountryId = $egypt; currencyId = $egp } $token
$null = Call POST "/admin/orders/$orderId/wallet/credit" @{ amount = 5000; note = 'test' } $adminToken

$created = Call POST '/applications' @{
  addressedTo = 'Ministry of Higher Education'; birthDate = '1990-05-17'
  names = @(@{ languageType = 0; firstName = 'أحمد'; lastName = 'علي' }, @{ languageType = 1; firstName = 'Ahmed'; lastName = 'Ali' })
  transactionTypeId = $txEducational; subTransactionTypeId = $subBachelor; verificationAuthorityId = $authority
  services = @(@{ serviceTypeId = $svcStandard; quantity = 1; languageCode = 'en'; isExpress = $false })
} $token
$appId = $created.Body.id
$lineId = $created.Body.services[0].id
$null = Upload $appId 'cert.pdf' $pdfBytes $lineId $reqStdCert $token
$null = Upload $appId 'id.png' $pngBytes $lineId $reqStdId $token
$null = Call POST "/applications/$appId/submit" $null $token
$null = Call POST '/payments' @{ applicationIds = @($appId) } $token

Write-Output "=== admin queue ==="
$queue = Call GET "/admin/applications?search=$($created.Body.applicationNumber)" $null $adminToken
Check 'paid application appears in the queue' ($queue.Status -eq 200 -and $queue.Body.totalCount -ge 1) "status $($queue.Status) total $($queue.Body.totalCount)"
$queueRow = @($queue.Body.items) | Where-Object { $_.id -eq $appId }
Check 'queue row carries order and authority context' ($queueRow.orderNumber -and $queueRow.authorityName) "orderNumber '$($queueRow.orderNumber)'"

$search = Call GET "/admin/applications?search=$($created.Body.applicationNumber)" $null $adminToken
Check 'queue search by application number works' ($search.Body.totalCount -eq 1) "total $($search.Body.totalCount)"

Write-Output "=== illegal transitions rejected ==="
$illegal = Call POST "/admin/applications/$appId/status" @{ toStatus = 5; note = 'skip ahead' } $adminToken
Check 'Pending -> Success refused (409)' ($illegal.Status -eq 409) "got $($illegal.Status)"
Check 'refusal carries illegal_status_transition' ($illegal.Body.code -eq 'application.illegal_status_transition') "got '$($illegal.Body.code)'"
$refunded = Call POST "/admin/applications/$appId/status" @{ toStatus = 7 } $adminToken
Check 'reviewer cannot set Refunded (400)' ($refunded.Status -eq 400) "got $($refunded.Status)"

Write-Output "=== Pending -> InProgress ==="
$start = Call POST "/admin/applications/$appId/status" @{ toStatus = 3; note = 'Started review' } $adminToken
Check 'review started' ($start.Status -eq 200 -and $start.Body.statusName -eq 'InProgress') "status $($start.Status) $($start.Body.statusName)"

Write-Output "=== internal comment does NOT change state ==="
$internal = Call POST "/admin/applications/$appId/comments" @{ body = $INTERNAL_SECRET; visibility = 1 } $adminToken
Check 'internal comment accepted' ($internal.Status -eq 200) "status $($internal.Status) $($internal.Raw)"
Check 'internal comment marked Internal' ($internal.Body.visibility -eq 1) "got $($internal.Body.visibility)"
$afterInternal = Call GET "/admin/applications/$appId" $null $adminToken
Check 'status still InProgress after internal note' ($afterInternal.Body.statusName -eq 'InProgress') "got $($afterInternal.Body.statusName)"

Write-Output "=== ForUser comment flips to MissedInfo (US4.1) ==="
$forUser = Call POST "/admin/applications/$appId/comments" @{ body = $FORUSER_TEXT; visibility = 0 } $adminToken
Check 'user-visible comment accepted' ($forUser.Status -eq 200) "status $($forUser.Status)"
$afterForUser = Call GET "/admin/applications/$appId" $null $adminToken
Check 'status auto-moved to MissedInfo' ($afterForUser.Body.statusName -eq 'MissedInfo') "got $($afterForUser.Body.statusName)"

Write-Output "=== CRITICAL: internal comments never reach the applicant (US4.2) ==="
$applicantTimeline = Call GET "/applications/$appId/timeline" $null $token
Check 'applicant timeline returns 200' ($applicantTimeline.Status -eq 200) "status $($applicantTimeline.Status)"
Check 'RAW applicant timeline does NOT contain the internal text' ($applicantTimeline.Raw -notmatch [regex]::Escape($INTERNAL_SECRET)) 'INTERNAL COMMENT LEAKED'
Check 'applicant timeline DOES contain the user-facing comment' ($applicantTimeline.Raw -match [regex]::Escape($FORUSER_TEXT)) 'user comment missing'
$applicantComments = @($applicantTimeline.Body) | Where-Object { $_.kindName -eq 'Comment' }
Check 'applicant sees exactly one comment' (@($applicantComments).Count -eq 1) "got $(@($applicantComments).Count)"
Check 'and it is the ForUser one' ($applicantComments[0].body -eq $FORUSER_TEXT) "got '$($applicantComments[0].body)'"

$applicantDetails = Call GET "/applications/$appId" $null $token
Check 'RAW applicant details does NOT contain the internal text' ($applicantDetails.Raw -notmatch [regex]::Escape($INTERNAL_SECRET)) 'INTERNAL COMMENT LEAKED'
$applicantList = Call GET '/applications' $null $token
Check 'RAW applicant list does NOT contain the internal text' ($applicantList.Raw -notmatch [regex]::Escape($INTERNAL_SECRET)) 'INTERNAL COMMENT LEAKED'
$results = Call GET "/applications/$appId/results" $null $token
Check 'RAW applicant results does NOT contain the internal text' ($results.Raw -notmatch [regex]::Escape($INTERNAL_SECRET)) 'INTERNAL COMMENT LEAKED'

$adminTimeline = Call GET "/admin/applications/$appId/timeline" $null $adminToken
Check 'admin timeline DOES contain the internal text' ($adminTimeline.Raw -match [regex]::Escape($INTERNAL_SECRET)) 'admin cannot see internal note'
$adminComments = @($adminTimeline.Body) | Where-Object { $_.kindName -eq 'Comment' }
Check 'admin sees both comments' (@($adminComments).Count -eq 2) "got $(@($adminComments).Count)"

Write-Output "=== applicant replies and resubmits ==="
$reply = Call POST "/applications/$appId/comments" @{ body = 'رفعت الصورة من جديد' } $token
Check 'applicant reply accepted' ($reply.Status -eq 200) "status $($reply.Status) $($reply.Raw)"
Check 'applicant reply forced to ForUser' ($reply.Body.visibility -eq 0) "got $($reply.Body.visibility)"

$reupload = Upload $appId 'clearer.png' $pngBytes $lineId $reqStdCert $token
Check 'applicant can re-upload while MissedInfo' ($reupload.Status -eq 200) "status $($reupload.Status) $($reupload.Raw)"

$resubmit = Call POST "/applications/$appId/resubmit" $null $token
Check 'resubmit moves back to InProgress' ($resubmit.Status -eq 200 -and $resubmit.Body.statusName -eq 'InProgress') "status $($resubmit.Status) $($resubmit.Body.statusName)"
$resubmitAgain = Call POST "/applications/$appId/resubmit" $null $token
Check 'resubmitting when not MissedInfo refused (409)' ($resubmitAgain.Status -eq 409) "got $($resubmitAgain.Status)"

Write-Output "=== results before success are refused ==="
$earlyResult = Upload $appId 'result.pdf' $pdfBytes $null $null $adminToken "/admin/applications/$appId/results"
Check 'result upload before Success refused (409)' ($earlyResult.Status -eq 409) "status $($earlyResult.Status)"

Write-Output "=== success and delivery (US4.4) ==="
$success = Call POST "/admin/applications/$appId/status" @{ toStatus = 5; note = 'Verified' } $adminToken
Check 'InProgress -> Success' ($success.Status -eq 200 -and $success.Body.statusName -eq 'Success') "status $($success.Status)"

$resultUpload = Upload $appId 'verified-certificate.pdf' $pdfBytes $null $null $adminToken "/admin/applications/$appId/results"
Check 'admin attaches the result file' ($resultUpload.Status -eq 200) "status $($resultUpload.Status) $($resultUpload.Raw)"

$applicantResults = Call GET "/applications/$appId/results" $null $token
Check 'applicant sees the result file' (@($applicantResults.Body).Count -eq 1) "got $(@($applicantResults.Body).Count)"
Check 'result file named correctly' ($applicantResults.Body[0].fileName -eq 'verified-certificate.pdf') "got '$($applicantResults.Body[0].fileName)'"

try {
  $dl = Invoke-WebRequest -Method GET -Uri "$base/applications/$appId/files/$($applicantResults.Body[0].id)" `
    -Headers @{ Authorization = "Bearer $token" } -UseBasicParsing -TimeoutSec 30
  Check 'applicant downloads the deliverable' ($dl.StatusCode -eq 200 -and $dl.RawContentLength -gt 0) "status $($dl.StatusCode)"
} catch { Check 'applicant downloads the deliverable' $false $_.Exception.Message }

Write-Output "=== timeline is merged and chronological (US4.3) ==="
$finalTimeline = Call GET "/applications/$appId/timeline" $null $token
$kinds = @($finalTimeline.Body) | ForEach-Object { $_.kindName } | Sort-Object -Unique
Check 'timeline merges comments, status changes and files' (($kinds -contains 'Comment') -and ($kinds -contains 'StatusChange') -and ($kinds -contains 'FileUploaded')) "kinds: $($kinds -join ',')"
Check 'timeline includes the attached result' ($kinds -contains 'ResultAttached') "kinds: $($kinds -join ',')"
$times = @($finalTimeline.Body) | ForEach-Object { [datetime]$_.createdAtUtc }
$sorted = $true
for ($i = 1; $i -lt $times.Count; $i++) { if ($times[$i] -lt $times[$i-1]) { $sorted = $false } }
Check 'timeline is in chronological order' $sorted 'entries out of order'
Check 'RAW final applicant timeline still free of internal text' ($finalTimeline.Raw -notmatch [regex]::Escape($INTERNAL_SECRET)) 'INTERNAL COMMENT LEAKED'

Write-Output "=== cross-realm and cross-order isolation ==="
$applicantOnAdmin = Call GET "/admin/applications/$appId" $null $token
Check 'applicant token refused on admin detail (403)' ($applicantOnAdmin.Status -eq 403) "got $($applicantOnAdmin.Status)"
$applicantStatusChange = Call POST "/admin/applications/$appId/status" @{ toStatus = 3 } $token
Check 'applicant cannot drive the review state machine (403)' ($applicantStatusChange.Status -eq 403) "got $($applicantStatusChange.Status)"

$otherEmail = "other$([guid]::NewGuid().ToString('N').Substring(0,8))@example.com"
$oreg = Call POST '/orders/register' @{ email = $otherEmail; languageCode = 'en' } $null
$ologin = Call POST '/orders/login' @{ orderNumber = $oreg.Body.orderNumber; password = $oreg.Body.password } $null
$otherToken = $ologin.Body.accessToken
Check "another order cannot read this timeline" ((Call GET "/applications/$appId/timeline" $null $otherToken).Status -eq 404) 'expected 404'
Check "another order cannot comment on it" ((Call POST "/applications/$appId/comments" @{ body = 'hi' } $otherToken).Status -eq 404) 'expected 404'
Check "another order cannot read its results" ((Call GET "/applications/$appId/results" $null $otherToken).Status -eq 404) 'expected 404'

Write-Output "=== audit trail (US5.3) ==="
$connectionString = 'Server=localhost;Database=DataVerification_Verify;Integrated Security=true;TrustServerCertificate=True;Encrypt=False'
$conn = New-Object System.Data.SqlClient.SqlConnection $connectionString
$conn.Open()
$cmd = $conn.CreateCommand()
$cmd.CommandText = "SELECT COUNT(DISTINCT Action) FROM AuditLog WHERE EntityId = '$appId'"
$actions = $cmd.ExecuteScalar()
$cmd.CommandText = "SELECT COUNT(*) FROM AuditLog WHERE EntityId = '$appId' AND Action = 'Application.StatusChanged'"
$statusAudits = $cmd.ExecuteScalar()
$conn.Close()
Check 'audit log records several distinct actions' ($actions -ge 5) "distinct actions: $actions"
Check 'every status change is audited' ($statusAudits -ge 4) "status audits: $statusAudits (expected >= 4)"

Write-Output ""
Write-Output "PASSED: $pass   FAILED: $fail"
if ($fail -gt 0) { exit 1 }
