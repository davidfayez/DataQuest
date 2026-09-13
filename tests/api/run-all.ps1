<#
.SYNOPSIS
    Runs every API verification suite against a running Data Verification API.

.DESCRIPTION
    These suites drive the real HTTP surface end to end — onboarding, the lookup cascade, the
    application aggregate, wallet payments and refunds, the review workflow, and the admin
    platform. They assert behaviour the unit tests cannot reach: status codes, ProblemDetails
    codes, cross-order isolation, and that internal comments never appear in an applicant payload.

    The API must already be running. Registration and sign-in are rate limited in production
    defaults, so raise the limits for an automated run:

        $env:RateLimiting__Registration__PermitLimit = "5000"
        $env:RateLimiting__Authentication__PermitLimit = "5000"
        dotnet run --project backend/src/DataVerification.API

.PARAMETER BaseUrl
    Root of the API, defaulting to the local development URL.

.EXAMPLE
    ./run-all.ps1
#>
[CmdletBinding()]
param(
    [string]$BaseUrl = 'http://localhost:5088/api/v1'
)

$ErrorActionPreference = 'Continue'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

try {
    $null = Invoke-WebRequest "$BaseUrl/health" -UseBasicParsing -TimeoutSec 5
} catch {
    Write-Error "The API is not reachable at $BaseUrl. Start it first."
    exit 1
}

# The RBAC suites need an admin who deliberately lacks some permissions.
& "$here\seed-reviewer.ps1" 2>$null | Out-Null

$suites = @(
    'verify-auth',
    'verify-lookups',
    'verify-applications',
    'verify-wallet',
    'verify-review',
    'verify-admin-platform'
)

$totalPassed = 0
$totalFailed = 0

foreach ($suite in $suites) {
    $output = & "$here\$suite.ps1"
    $summary = ($output | Select-String -Pattern '^PASSED:').ToString()

    if ($summary -match 'PASSED: (\d+)\s+FAILED: (\d+)') {
        $passed = [int]$Matches[1]
        $failed = [int]$Matches[2]
        $totalPassed += $passed
        $totalFailed += $failed

        Write-Output ("{0,-24} {1,4} passed, {2} failed" -f $suite, $passed, $failed)
        $output | Select-String -Pattern '^  FAIL' | ForEach-Object { Write-Output "    $_" }
    } else {
        Write-Output "$suite — could not parse a summary; the suite may have crashed."
        $totalFailed++
    }
}

Write-Output ''
Write-Output "TOTAL: $totalPassed passed, $totalFailed failed"

if ($totalFailed -gt 0) { exit 1 }
