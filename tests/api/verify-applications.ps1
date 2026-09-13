$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5088/api/v1'
$egypt = '22222222-0000-0000-0000-000000000001'
$egp = '22222222-0000-0000-0000-000000000002'
$txEducational = '44444444-0000-0000-0000-000000000001'
$subBachelor = '55555555-0000-0000-0000-000000000001'
$subCriminal = '55555555-0000-0000-0000-000000000005'
$authority = '66666666-0000-0000-0000-000000000001'
$svcStandard = '77777777-0000-0000-0000-000000000001'   # cost 750, no express, 2 mandatory files
$svcAttested = '77777777-0000-0000-0000-000000000002'   # cost 1500, express +600
$reqStdCert = '88888888-0000-0000-0000-000000000001'
$reqStdId = '88888888-0000-0000-0000-000000000002'
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

# Builds a multipart/form-data upload by hand: PowerShell 5.1 has no -Form parameter.
function Upload($applicationId, $fileName, [byte[]]$bytes, $serviceId, $requiredFileId, $token) {
  $boundary = [guid]::NewGuid().ToString()
  $nl = "`r`n"
  $ms = New-Object System.IO.MemoryStream
  function Append([string]$s) { $b = [System.Text.Encoding]::UTF8.GetBytes($s); $ms.Write($b, 0, $b.Length) }

  if ($serviceId) {
    Append "--$boundary$nl"
    Append "Content-Disposition: form-data; name=`"applicationServiceId`"$nl$nl$serviceId$nl"
  }
  if ($requiredFileId) {
    Append "--$boundary$nl"
    Append "Content-Disposition: form-data; name=`"requiredFileId`"$nl$nl$requiredFileId$nl"
  }
  Append "--$boundary$nl"
  Append "Content-Disposition: form-data; name=`"file`"; filename=`"$fileName`"${nl}Content-Type: application/octet-stream$nl$nl"
  $ms.Write($bytes, 0, $bytes.Length)
  Append "$nl--$boundary--$nl"

  try {
    $r = Invoke-WebRequest -Method POST -Uri "$base/applications/$applicationId/files" `
      -Headers @{ 'Authorization' = "Bearer $token"; 'Accept' = 'application/json' } `
      -ContentType "multipart/form-data; boundary=$boundary" -Body $ms.ToArray() -UseBasicParsing -TimeoutSec 60
    return @{ Status = [int]$r.StatusCode; Body = ($r.Content | ConvertFrom-Json) }
  } catch {
    $resp = $_.Exception.Response
    $status = if ($resp) { [int]$resp.StatusCode } else { 0 }
    $text = ''
    if ($resp) { $text = (New-Object System.IO.StreamReader($resp.GetResponseStream())).ReadToEnd() }
    $parsed = $null
    if ($text) { try { $parsed = $text | ConvertFrom-Json } catch { } }
    return @{ Status = $status; Body = $parsed; Raw = $text }
  } finally { $ms.Dispose() }
}

function NewOrder($label) {
  $email = "$label$([guid]::NewGuid().ToString('N').Substring(0,8))@example.com"
  $reg = Call POST '/orders/register' @{ email = $email; languageCode = 'en' } $null
  $login = Call POST '/orders/login' @{ orderNumber = $reg.Body.orderNumber; password = $reg.Body.password } $null
  $t = $login.Body.accessToken
  $null = Call PUT '/orders/setup' @{ verificationCountryId = $egypt; currencyId = $egp } $t
  return $t
}

# Real file signatures, so the magic-byte validator is exercised for real.
$pngBytes = [byte[]](0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A) + [byte[]](1..64)
$pdfBytes = [System.Text.Encoding]::ASCII.GetBytes("%PDF-1.4`n% test document`n") + [byte[]](1..64)
$textBytes = [System.Text.Encoding]::ASCII.GetBytes("this is definitely not a pdf")

$token = NewOrder 'appuser'

function BaseApplication($serviceTypeId, $isExpress, $subTypeId) {
  return @{
    addressedTo = 'الطلب موجه إلي وزارة التعليم العالي'
    birthDate = '1990-05-17'
    names = @(
      @{ languageType = 0; firstName = 'أحمد'; middleName = 'محمد'; lastName = 'علي' },
      @{ languageType = 1; firstName = 'Ahmed'; middleName = 'Mohamed'; lastName = 'Ali' }
    )
    transactionTypeId = $txEducational
    subTransactionTypeId = $subTypeId
    verificationAuthorityId = $authority
    services = @(@{ serviceTypeId = $serviceTypeId; quantity = 1; languageCode = 'en'; isExpress = $isExpress })
  }
}

Write-Output "=== create application (server-side pricing) ==="
$create = Call POST '/applications' (BaseApplication $svcStandard $false $subBachelor) $token
Check 'application created (201)' ($create.Status -eq 201) "status $($create.Status) $($create.Raw)"
$appId = $create.Body.id
Check 'status is Draft' ($create.Body.statusName -eq 'Draft') "got $($create.Body.statusName)"
Check 'server computed total = 750' ($create.Body.totalCost -eq 750) "got $($create.Body.totalCost)"
Check 'application number formatted APP-YYMM-XXXXXX' ($create.Body.applicationNumber -match '^APP-\d{4}-[A-Z2-9]{6}$') "got $($create.Body.applicationNumber)"
Check 'both name rows stored' (@($create.Body.names).Count -eq 2) "got $(@($create.Body.names).Count)"
Check 'Arabic name survived round-trip' ($create.Body.names[0].firstName -eq 'أحمد') "got '$($create.Body.names[0].firstName)'"
Check 'capability flags: editable while Draft' ($create.Body.canEdit -eq $true -and $create.Body.canDelete -eq $true) "canEdit $($create.Body.canEdit)"
Check 'capability flags: not refundable while unpaid' ($create.Body.canRefund -eq $false) "canRefund $($create.Body.canRefund)"
Check 'required-file checklist exposed' (@($create.Body.requiredFiles).Count -eq 2) "got $(@($create.Body.requiredFiles).Count)"
Check 'no required file satisfied yet' ((@($create.Body.requiredFiles) | Where-Object { $_.isSatisfied }).Count -eq 0) 'unexpected satisfied file'

Write-Output "=== express pricing and rejection (US2.4) ==="
$expressOk = Call POST '/applications' (BaseApplication $svcAttested $true $subBachelor) $token
Check 'express allowed on express-enabled service' ($expressOk.Status -eq 201) "status $($expressOk.Status) $($expressOk.Raw)"
Check 'express total = (1500 + 600) x 1' ($expressOk.Body.totalCost -eq 2100) "got $($expressOk.Body.totalCost)"
$expressBad = Call POST '/applications' (BaseApplication $svcStandard $true $subBachelor) $token
Check 'express rejected on non-express service' ($expressBad.Status -eq 409) "status $($expressBad.Status)"
Check 'rejection carries service.express_not_available' ($expressBad.Body.code -eq 'service.express_not_available') "got '$($expressBad.Body.code)'"

Write-Output "=== cascade validation (US2.3) ==="
$wrongSub = BaseApplication $svcStandard $false $subCriminal
$wrongSub.transactionTypeId = '44444444-0000-0000-0000-000000000003'
$wrongSubRes = Call POST '/applications' $wrongSub $token
Check 'authority not mapped to sub-type is rejected' ($wrongSubRes.Status -eq 409) "status $($wrongSubRes.Status)"
Check 'rejection carries authority_not_available' ($wrongSubRes.Body.code -eq 'application.authority_not_available') "got '$($wrongSubRes.Body.code)'"

$mismatch = BaseApplication $svcStandard $false $subBachelor
$mismatch.transactionTypeId = '44444444-0000-0000-0000-000000000003'
$mismatchRes = Call POST '/applications' $mismatch $token
Check 'sub-type not under the given transaction type is rejected' ($mismatchRes.Status -eq 409) "status $($mismatchRes.Status)"
Check 'mismatch carries sub_transaction_type_mismatch' ($mismatchRes.Body.code -eq 'application.sub_transaction_type_mismatch') "got '$($mismatchRes.Body.code)'"

Write-Output "=== field validation (US2.1 / US2.2) ==="
$future = BaseApplication $svcStandard $false $subBachelor
$future.birthDate = '2099-01-01'
Check 'future birth date rejected' ((Call POST '/applications' $future $token).Status -eq 400) 'expected 400'

$oneName = BaseApplication $svcStandard $false $subBachelor
$oneName.names = @(@{ languageType = 1; firstName = 'Ahmed'; lastName = 'Ali' })
Check 'missing Arabic name rejected' ((Call POST '/applications' $oneName $token).Status -eq 400) 'expected 400'

$noLast = BaseApplication $svcStandard $false $subBachelor
$noLast.names[0].lastName = ''
Check 'missing Arabic last name rejected' ((Call POST '/applications' $noLast $token).Status -eq 400) 'expected 400'

Write-Output "=== file upload validation (US2.5) ==="
$serviceLineId = $create.Body.services[0].id

$disguised = Upload $appId 'malware.pdf' $textBytes $serviceLineId $reqStdCert $token
Check 'text file renamed .pdf rejected by magic-byte check' ($disguised.Status -eq 415) "status $($disguised.Status)"
Check 'rejection carries file.unsupported_type' ($disguised.Body.code -eq 'file.unsupported_type') "got '$($disguised.Body.code)'"

$pngAsPdf = Upload $appId 'scan.pdf' $pngBytes $serviceLineId $reqStdCert $token
Check 'PNG bytes with .pdf extension rejected' ($pngAsPdf.Status -eq 415) "status $($pngAsPdf.Status)"

$exe = Upload $appId 'tool.exe' $pdfBytes $serviceLineId $reqStdCert $token
Check 'disallowed extension rejected' ($exe.Status -eq 415) "status $($exe.Status)"

$tooBig = Upload $appId 'huge.png' ($pngBytes + [byte[]]::new(5MB)) $serviceLineId $reqStdCert $token
Check 'file larger than 5 MB rejected' ($tooBig.Status -eq 409 -or $tooBig.Status -eq 413) "status $($tooBig.Status)"

Write-Output "=== upload security gate ==="
$enc = [System.Text.Encoding]::ASCII

# A PDF that runs something on open is not the passive document it claims to be.
$activePdf = $enc.GetBytes("%PDF-1.4`n/OpenAction << /S /JavaScript /JS (app.alert(1)) >>`n") + [byte[]]::new(200)
$pdfJs = Upload $appId 'payload.pdf' $activePdf $serviceLineId $reqStdCert $token
Check 'PDF carrying JavaScript rejected' ($pdfJs.Status -eq 422 -and $pdfJs.Body.code -eq 'file.active_content') "status $($pdfJs.Status) code $($pdfJs.Body.code)"

# A valid PNG header with markup appended: renders as an image, executes if served as HTML.
$scriptPng = $pngBytes + $enc.GetBytes('<script>alert(1)</script>') + [byte[]]::new(64)
$pngScript = Upload $appId 'shot.png' $scriptPng $serviceLineId $reqStdCert $token
Check 'image with appended script rejected' ($pngScript.Status -eq 422 -and $pngScript.Body.code -eq 'file.active_content') "status $($pngScript.Status) code $($pngScript.Body.code)"

# A Windows executable smuggled after a valid image header.
$droppedExe = $pngBytes + [byte[]]::new(32) + $enc.GetBytes('MZ') + [byte[]]::new(64)
$pngExe = Upload $appId 'shot2.png' $droppedExe $serviceLineId $reqStdCert $token
Check 'image with appended executable rejected' ($pngExe.Status -eq 422 -and $pngExe.Body.code -eq 'file.executable_content') "status $($pngExe.Status) code $($pngExe.Body.code)"

$doubleExt = Upload $appId 'invoice.php.pdf' $pdfBytes $serviceLineId $reqStdCert $token
Check 'double extension rejected' ($doubleExt.Status -eq 415 -and $doubleExt.Body.code -eq 'file.double_extension') "status $($doubleExt.Status) code $($doubleExt.Body.code)"

$traversal = Upload $appId '../../etc/passwd.pdf' $pdfBytes $serviceLineId $reqStdCert $token
Check 'path traversal in the file name rejected' ($traversal.Status -eq 400 -and $traversal.Body.code -eq 'file.invalid_name') "status $($traversal.Status) code $($traversal.Body.code)"

Write-Output "=== submission gated on mandatory files ==="
$earlySubmit = Call POST "/applications/$appId/submit" $null $token
Check 'submit blocked while mandatory files missing' ($earlySubmit.Status -eq 409) "status $($earlySubmit.Status)"
Check 'block carries mandatory_files_missing' ($earlySubmit.Body.code -eq 'application.mandatory_files_missing') "got '$($earlySubmit.Body.code)'"

$up1 = Upload $appId 'certificate.pdf' $pdfBytes $serviceLineId $reqStdCert $token
Check 'valid PDF accepted' ($up1.Status -eq 200) "status $($up1.Status) $($up1.Raw)"
Check 'content type detected as application/pdf' ($up1.Body.contentType -eq 'application/pdf') "got '$($up1.Body.contentType)'"
$up2 = Upload $appId 'id-card.png' $pngBytes $serviceLineId $reqStdId $token
Check 'valid PNG accepted' ($up2.Status -eq 200) "status $($up2.Status)"
Check 'content type detected as image/png' ($up2.Body.contentType -eq 'image/png') "got '$($up2.Body.contentType)'"

$afterUploads = Call GET "/applications/$appId" $null $token
Check 'checklist now fully satisfied' ((@($afterUploads.Body.requiredFiles) | Where-Object { -not $_.isSatisfied }).Count -eq 0) 'still unsatisfied'

$submit = Call POST "/applications/$appId/submit" $null $token
Check 'submit succeeds once documented' ($submit.Status -eq 200) "status $($submit.Status) $($submit.Raw)"
Check 'status becomes PendingPayment' ($submit.Body.statusName -eq 'PendingPayment') "got $($submit.Body.statusName)"
Check 'still editable while unpaid' ($submit.Body.canEdit -eq $true) "canEdit $($submit.Body.canEdit)"

Write-Output "=== download ==="
try {
  $dl = Invoke-WebRequest -Method GET -Uri "$base/applications/$appId/files/$($up1.Body.id)" `
    -Headers @{ 'Authorization' = "Bearer $token" } -UseBasicParsing -TimeoutSec 30
  Check 'owner can download their file' ($dl.StatusCode -eq 200 -and $dl.RawContentLength -gt 0) "status $($dl.StatusCode)"
} catch { Check 'owner can download their file' $false $_.Exception.Message }

Write-Output "=== update and delete ==="
$updateBody = BaseApplication $svcStandard $false $subBachelor
$updateBody.addressedTo = 'Updated addressee'
$update = Call PUT "/applications/$appId" $updateBody $token
Check 'unpaid application can be updated' ($update.Status -eq 200) "status $($update.Status) $($update.Raw)"
Check 'update applied' ($update.Body.addressedTo -eq 'Updated addressee') "got '$($update.Body.addressedTo)'"

$throwaway = Call POST '/applications' (BaseApplication $svcStandard $false $subBachelor) $token
$del = Call DELETE "/applications/$($throwaway.Body.id)" $null $token
Check 'unpaid application can be deleted' ($del.Status -eq 204) "status $($del.Status)"
Check 'deleted application no longer readable' ((Call GET "/applications/$($throwaway.Body.id)" $null $token).Status -eq 404) 'expected 404'

Write-Output "=== order isolation (an order can never read another order's data) ==="
$otherToken = NewOrder 'otheruser'
Check "other order gets 404 on this application" ((Call GET "/applications/$appId" $null $otherToken).Status -eq 404) 'expected 404'
Check "other order cannot update it" ((Call PUT "/applications/$appId" $updateBody $otherToken).Status -eq 404) 'expected 404'
Check "other order cannot delete it" ((Call DELETE "/applications/$appId" $null $otherToken).Status -eq 404) 'expected 404'
Check "other order cannot download the file" ((Call GET "/applications/$appId/files/$($up1.Body.id)" $null $otherToken).Status -eq 404) 'expected 404'
$otherList = Call GET '/applications' $null $otherToken
Check "other order's list is empty" ($otherList.Body.totalCount -eq 0) "got $($otherList.Body.totalCount)"

Write-Output "=== listing ==="
$list = Call GET '/applications' $null $token
Check 'list returns the caller applications' ($list.Status -eq 200 -and $list.Body.totalCount -ge 2) "total $($list.Body.totalCount)"
Check 'list rows carry capability flags' ($null -ne $list.Body.items[0].canEdit) 'flags missing'
$filtered = Call GET '/applications?status=1' $null $token
Check 'status filter works (PendingPayment)' ($filtered.Body.totalCount -ge 1) "total $($filtered.Body.totalCount)"

Write-Output ""
Write-Output "PASSED: $pass   FAILED: $fail"
if ($fail -gt 0) { exit 1 }
