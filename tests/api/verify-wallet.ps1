$ErrorActionPreference = 'Continue'
$base = 'http://localhost:5088/api/v1'
$egypt = '22222222-0000-0000-0000-000000000001'
$egp = '22222222-0000-0000-0000-000000000002'
$txEducational = '44444444-0000-0000-0000-000000000001'
$subBachelor = '55555555-0000-0000-0000-000000000001'
$authority = '66666666-0000-0000-0000-000000000001'
$svcStandard = '77777777-0000-0000-0000-000000000001'   # 750
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

function Upload($applicationId, $fileName, [byte[]]$bytes, $serviceId, $requiredFileId, $token) {
  $boundary = [guid]::NewGuid().ToString()
  $nl = "`r`n"
  $ms = New-Object System.IO.MemoryStream
  function Append([string]$s) { $b = [System.Text.Encoding]::UTF8.GetBytes($s); $ms.Write($b, 0, $b.Length) }
  Append "--$boundary$nl"
  Append "Content-Disposition: form-data; name=`"applicationServiceId`"$nl$nl$serviceId$nl"
  Append "--$boundary$nl"
  Append "Content-Disposition: form-data; name=`"requiredFileId`"$nl$nl$requiredFileId$nl"
  Append "--$boundary$nl"
  Append "Content-Disposition: form-data; name=`"file`"; filename=`"$fileName`"${nl}Content-Type: application/octet-stream$nl$nl"
  $ms.Write($bytes, 0, $bytes.Length)
  Append "$nl--$boundary--$nl"
  try {
    $r = Invoke-WebRequest -Method POST -Uri "$base/applications/$applicationId/files" `
      -Headers @{ 'Authorization' = "Bearer $token" } `
      -ContentType "multipart/form-data; boundary=$boundary" -Body $ms.ToArray() -UseBasicParsing -TimeoutSec 60
    return [int]$r.StatusCode
  } catch { return 0 } finally { $ms.Dispose() }
}

$pdfBytes = [System.Text.Encoding]::ASCII.GetBytes("%PDF-1.4`n% doc`n") + [byte[]](1..32)
$pngBytes = [byte[]](0x89,0x50,0x4E,0x47,0x0D,0x0A,0x1A,0x0A) + [byte[]](1..32)

function NewOrder($label) {
  $email = "$label$([guid]::NewGuid().ToString('N').Substring(0,8))@example.com"
  $reg = Call POST '/orders/register' @{ email = $email; languageCode = 'en' } $null
  $login = Call POST '/orders/login' @{ orderNumber = $reg.Body.orderNumber; password = $reg.Body.password } $null
  $t = $login.Body.accessToken
  $null = Call PUT '/orders/setup' @{ verificationCountryId = $egypt; currencyId = $egp } $t
  return @{ Token = $t; OrderId = $login.Body.orderId }
}

# Creates a fully documented application and submits it, leaving it at PendingPayment.
function NewSubmittedApplication($token) {
  $body = @{
    addressedTo = 'Ministry of Higher Education'
    birthDate = '1990-05-17'
    names = @(
      @{ languageType = 0; firstName = 'أحمد'; middleName = $null; lastName = 'علي' },
      @{ languageType = 1; firstName = 'Ahmed'; middleName = $null; lastName = 'Ali' }
    )
    transactionTypeId = $txEducational
    subTransactionTypeId = $subBachelor
    verificationAuthorityId = $authority
    services = @(@{ serviceTypeId = $svcStandard; quantity = 1; languageCode = 'en'; isExpress = $false })
  }
  $created = Call POST '/applications' $body $token
  $appId = $created.Body.id
  $lineId = $created.Body.services[0].id
  $null = Upload $appId 'cert.pdf' $pdfBytes $lineId $reqStdCert $token
  $null = Upload $appId 'id.png' $pngBytes $lineId $reqStdId $token
  $null = Call POST "/applications/$appId/submit" $null $token
  return $appId
}

$admin = Call POST '/admin/auth/login' @{ email = 'admin@dataverification.local'; password = 'Admin#12345' } $null
$adminToken = $admin.Body.accessToken

$order = NewOrder 'payer'
$token = $order.Token
$orderId = $order.OrderId

Write-Output "=== wallet starts empty ==="
$wallet0 = Call GET '/orders/me/wallet' $null $token
Check 'wallet readable' ($wallet0.Status -eq 200) "status $($wallet0.Status) $($wallet0.Raw)"
Check 'balance starts at zero' ($wallet0.Body.wallet.balance -eq 0) "got $($wallet0.Body.wallet.balance)"
Check 'currency is EGP' ($wallet0.Body.wallet.currencyCode -eq 'EGP') "got $($wallet0.Body.wallet.currencyCode)"
Check 'ledger starts empty' ($wallet0.Body.ledger.totalCount -eq 0) "got $($wallet0.Body.ledger.totalCount)"

Write-Output "=== admin credit (Orders.Credit) ==="
$credit = Call POST "/admin/orders/$orderId/wallet/credit" @{ amount = 2500; note = 'Initial top-up' } $adminToken
Check 'admin credits the wallet' ($credit.Status -eq 200) "status $($credit.Status) $($credit.Raw)"
Check 'balance reflects the credit' ($credit.Body.balance -eq 2500) "got $($credit.Body.balance)"

$reviewer = Call POST '/admin/auth/login' @{ email = 'reviewer@dataverification.local'; password = 'Admin#12345' } $null
if ($reviewer.Status -eq 200) {
  $rc = Call POST "/admin/orders/$orderId/wallet/credit" @{ amount = 100; note = 'nope' } $reviewer.Body.accessToken
  Check 'reviewer without Orders.Credit is refused (403)' ($rc.Status -eq 403) "got $($rc.Status)"
}

Write-Output "=== pay three applications together (US3.1) ==="
$app1 = NewSubmittedApplication $token
$app2 = NewSubmittedApplication $token
$app3 = NewSubmittedApplication $token

$pay = Call POST '/payments' @{ applicationIds = @($app1, $app2, $app3) } $token
Check 'batch payment succeeds' ($pay.Status -eq 200) "status $($pay.Status) $($pay.Raw)"
Check 'charged 3 x 750 = 2250' ($pay.Body.amountPaid -eq 2250) "got $($pay.Body.amountPaid)"
Check 'balance now 250' ($pay.Body.balanceAfter -eq 250) "got $($pay.Body.balanceAfter)"
Check 'all three reported as paid' (@($pay.Body.applications).Count -eq 3) "got $(@($pay.Body.applications).Count)"
Check 'each moved to Pending' ((@($pay.Body.applications) | Where-Object { $_.status -ne 2 }).Count -eq 0) 'not all Pending'

$walletAfter = Call GET '/orders/me/wallet' $null $token
Check 'ledger has exactly 2 entries (credit + one payment)' ($walletAfter.Body.ledger.totalCount -eq 2) "got $($walletAfter.Body.ledger.totalCount)"
$payment = @($walletAfter.Body.ledger.items) | Where-Object { $_.typeName -eq 'Payment' }
Check 'single payment entry references all 3 applications' (@($payment.referenceApplicationIds).Count -eq 3) "got $(@($payment.referenceApplicationIds).Count)"
Check 'payment amount is signed negative' ($payment.signedAmount -eq -2250) "got $($payment.signedAmount)"
Check 'ledger shows application numbers, not raw ids' ($payment.referenceApplicationNumbers[0] -match '^APP-') "got '$($payment.referenceApplicationNumbers[0])'"

Write-Output "=== ledger reconciles with balance ==="
$sum = 0
foreach ($item in @($walletAfter.Body.ledger.items)) { $sum += $item.signedAmount }
Check 'sum of signed ledger equals balance' ($sum -eq $walletAfter.Body.wallet.balance) "ledger $sum vs balance $($walletAfter.Body.wallet.balance)"

Write-Output "=== post-payment lockdown (US3.3) ==="
$updateBody = @{
  addressedTo = 'Changed after payment'; birthDate = '1990-05-17'
  names = @(@{ languageType = 0; firstName = 'أحمد'; lastName = 'علي' }, @{ languageType = 1; firstName = 'Ahmed'; lastName = 'Ali' })
  transactionTypeId = $txEducational; subTransactionTypeId = $subBachelor; verificationAuthorityId = $authority
  services = @(@{ serviceTypeId = $svcStandard; quantity = 1; languageCode = 'en'; isExpress = $false })
}
$editPaid = Call PUT "/applications/$app1" $updateBody $token
Check 'editing a paid application is refused (409)' ($editPaid.Status -eq 409) "got $($editPaid.Status)"
Check 'refusal carries application.not_editable' ($editPaid.Body.code -eq 'application.not_editable') "got '$($editPaid.Body.code)'"
$deletePaid = Call DELETE "/applications/$app1" $null $token
Check 'deleting a paid application is refused (409)' ($deletePaid.Status -eq 409) "got $($deletePaid.Status)"

$detail = Call GET "/applications/$app1" $null $token
Check 'capability flags say not editable' ($detail.Body.canEdit -eq $false -and $detail.Body.canDelete -eq $false) "canEdit $($detail.Body.canEdit)"
Check 'capability flags say refundable' ($detail.Body.canRefund -eq $true) "canRefund $($detail.Body.canRefund)"

Write-Output "=== refund (US3.2) ==="
$refund = Call POST "/applications/$app1/refund" @{ note = 'Applicant changed their mind' } $token
Check 'refund succeeds while Pending' ($refund.Status -eq 200) "status $($refund.Status) $($refund.Raw)"
Check 'refund returns 750' ($refund.Body.amountRefunded -eq 750) "got $($refund.Body.amountRefunded)"
Check 'balance back to 1000' ($refund.Body.balanceAfter -eq 1000) "got $($refund.Body.balanceAfter)"
Check 'status becomes Refunded' ($refund.Body.status -eq 7) "got $($refund.Body.status)"

$refundAgain = Call POST "/applications/$app1/refund" $null $token
Check 'second refund refused (409)' ($refundAgain.Status -eq 409) "got $($refundAgain.Status)"
Check 'refusal carries application.not_refundable' ($refundAgain.Body.code -eq 'application.not_refundable') "got '$($refundAgain.Body.code)'"

Write-Output "=== atomicity: a bad item rolls the whole batch back ==="
$balanceBefore = (Call GET '/orders/me/wallet' $null $token).Body.wallet.balance
$app4 = NewSubmittedApplication $token
$mixed = Call POST '/payments' @{ applicationIds = @($app4, $app2) } $token   # app2 is already paid
Check 'batch containing an already-paid application is refused' ($mixed.Status -eq 409) "got $($mixed.Status)"
$balanceAfterFailed = (Call GET '/orders/me/wallet' $null $token).Body.wallet.balance
Check 'wallet untouched by the failed batch' ($balanceAfterFailed -eq $balanceBefore) "before $balanceBefore after $balanceAfterFailed"
$app4State = Call GET "/applications/$app4" $null $token
Check 'the valid application in the batch was not charged' ($app4State.Body.statusName -eq 'PendingPayment') "got $($app4State.Body.statusName)"

Write-Output "=== insufficient funds (422 with required vs available) ==="
$drain = Call POST '/payments' @{ applicationIds = @($app4) } $token
Check 'pay app4 to draw the balance down' ($drain.Status -eq 200) "status $($drain.Status)"
$app5 = NewSubmittedApplication $token
$app6 = NewSubmittedApplication $token
$broke = Call POST '/payments' @{ applicationIds = @($app5, $app6) } $token
Check 'insufficient balance returns 422' ($broke.Status -eq 422) "got $($broke.Status)"
Check 'error carries wallet.insufficient_funds' ($broke.Body.code -eq 'wallet.insufficient_funds') "got '$($broke.Body.code)'"
Check 'error reports required amount' ($broke.Body.required -eq 1500) "got $($broke.Body.required)"
Check 'error reports available amount' ($null -ne $broke.Body.available) "missing available"

Write-Output "=== payment guards ==="
$draftBody = $updateBody.Clone()
$draftApp = Call POST '/applications' $draftBody $token
$payDraft = Call POST '/payments' @{ applicationIds = @($draftApp.Body.id) } $token
Check 'paying an unsubmitted Draft is refused (409)' ($payDraft.Status -eq 409) "got $($payDraft.Status)"

$other = NewOrder 'otherpayer'
$crossPay = Call POST '/payments' @{ applicationIds = @($app5) } $other.Token
Check "paying another order's application returns 404" ($crossPay.Status -eq 404) "got $($crossPay.Status)"
$crossRefund = Call POST "/applications/$app2/refund" $null $other.Token
Check "refunding another order's application returns 404" ($crossRefund.Status -eq 404) "got $($crossRefund.Status)"

$dupe = Call POST '/payments' @{ applicationIds = @($app5, $app5) } $token
Check 'duplicate ids in one batch rejected (400)' ($dupe.Status -eq 400) "got $($dupe.Status)"
$empty = Call POST '/payments' @{ applicationIds = @() } $token
Check 'empty batch rejected (400)' ($empty.Status -eq 400) "got $($empty.Status)"

Write-Output "=== admin refund path (Orders.Refund) ==="
$adminCredit = Call POST "/admin/orders/$orderId/wallet/credit" @{ amount = 5000; note = 'top up' } $adminToken
$payForAdminRefund = Call POST '/payments' @{ applicationIds = @($app5) } $token
Check 'application paid for admin-refund test' ($payForAdminRefund.Status -eq 200) "status $($payForAdminRefund.Status)"
$adminRefund = Call POST "/admin/orders/$orderId/applications/$app5/refund" @{ note = 'Refunded by support' } $adminToken
Check 'admin can refund a Pending application' ($adminRefund.Status -eq 200) "status $($adminRefund.Status) $($adminRefund.Raw)"
Check 'admin refund credits the wallet' ($adminRefund.Body.amountRefunded -eq 750) "got $($adminRefund.Body.amountRefunded)"

$adminWallet = Call GET "/admin/orders/$orderId/wallet" $null $adminToken
Check 'admin can read the order ledger' ($adminWallet.Status -eq 200 -and $adminWallet.Body.ledger.totalCount -gt 0) "status $($adminWallet.Status)"

Write-Output ""
Write-Output "PASSED: $pass   FAILED: $fail"
if ($fail -gt 0) { exit 1 }
