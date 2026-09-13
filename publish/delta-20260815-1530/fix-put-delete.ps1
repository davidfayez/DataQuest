<#
    Unblocks PUT and DELETE on the API site, then verifies the result.

    IIS ships a WebDAV module and handler that claim PUT and DELETE before ASP.NET Core sees them,
    answering "405 Method Not Allowed / Allow: GET, HEAD, OPTIONS, TRACE". GET and POST are
    unaffected, so the site looks healthy while order setup, saving the SendGrid key, editing an
    application and every delete in the admin panel all fail.

    Applies the fix at three levels, because a web.config edit alone is ignored when the modules
    section is locked at server level:

      1. web.config   - remove the WebDAV handler and module for this site
      2. appcmd       - same removal written through IIS configuration (needs -SiteName)
      3. the feature  - uninstall IIS-WebDAV entirely (only with -DisableWebDavFeature)

    Everything else in web.config is preserved, especially <environmentVariables>, which holds the
    connection string, Jwt__SigningKey and Security__EncryptionKey. A timestamped backup is written
    before any change. Safe to run repeatedly.

    Run in an ELEVATED PowerShell on the IIS server:

        .\fix-put-delete.ps1 -AppPool "<your api pool>" -SiteName "<your api site>"

    If PUT is still 405 afterwards, run again adding -DisableWebDavFeature.
#>
[CmdletBinding()]
param(
    [string] $Path = 'E:\inetpub\DataVerification\api\web.config',
    [string] $AppPool,
    [string] $SiteName,
    [switch] $DisableWebDavFeature,
    [string] $VerifyUrl = 'https://vdataapi.nen-global.org/api/v1/orders/setup'
)

$ErrorActionPreference = 'Stop'
function Say($text, $colour = 'Gray') { Write-Host $text -ForegroundColor $colour }

Say "`n=== 1. web.config ===" 'Cyan'

if (-not (Test-Path $Path)) { throw "No web.config at '$Path'. Pass -Path with the correct location." }

$xml = New-Object System.Xml.XmlDocument
$xml.PreserveWhitespace = $true
$xml.Load((Resolve-Path $Path))

$server = $xml.SelectSingleNode('/configuration/location/system.webServer')
if (-not $server) { $server = $xml.SelectSingleNode('/configuration/system.webServer') }
if (-not $server) { throw 'No <system.webServer> section found. Is this the API site web.config?' }

function Ensure-Child($parent, $name) {
    $node = $parent.SelectSingleNode($name)
    if (-not $node) { $node = $xml.CreateElement($name); $parent.AppendChild($node) | Out-Null }
    return $node
}

$changed = $false

$handlers = Ensure-Child $server 'handlers'
$removeWebDav = $handlers.SelectSingleNode("remove[@name='WebDAV']")
if (-not $removeWebDav) {
    $removeWebDav = $xml.CreateElement('remove')
    $removeWebDav.SetAttribute('name', 'WebDAV')
    if ($handlers.FirstChild) { $handlers.InsertBefore($removeWebDav, $handlers.FirstChild) | Out-Null }
    else { $handlers.AppendChild($removeWebDav) | Out-Null }
    Say '  added   <handlers><remove name="WebDAV" />' 'Green'; $changed = $true
} elseif ($handlers.FirstChild -and -not [object]::ReferenceEquals($handlers.FirstChild, $removeWebDav)) {
    # Present but after the aspNetCore handler, where it has no effect.
    $handlers.RemoveChild($removeWebDav) | Out-Null
    $handlers.InsertBefore($removeWebDav, $handlers.FirstChild) | Out-Null
    Say '  moved   <remove name="WebDAV" /> ahead of the aspNetCore handler' 'Green'; $changed = $true
} else { Say '  present <handlers><remove name="WebDAV" />' }

if (-not $handlers.SelectSingleNode("add[@name='aspNetCore']")) {
    $add = $xml.CreateElement('add')
    $add.SetAttribute('name', 'aspNetCore'); $add.SetAttribute('path', '*'); $add.SetAttribute('verb', '*')
    $add.SetAttribute('modules', 'AspNetCoreModuleV2'); $add.SetAttribute('resourceType', 'Unspecified')
    $handlers.AppendChild($add) | Out-Null
    Say '  added   <add name="aspNetCore" ... />' 'Green'; $changed = $true
}

$modules = Ensure-Child $server 'modules'
if (-not $modules.SelectSingleNode("remove[@name='WebDAVModule']")) {
    $rm = $xml.CreateElement('remove'); $rm.SetAttribute('name', 'WebDAVModule')
    $modules.AppendChild($rm) | Out-Null
    Say '  added   <modules><remove name="WebDAVModule" />' 'Green'; $changed = $true
} else { Say '  present <modules><remove name="WebDAVModule" />' }

Say "`n=== 1b. Required secrets ===" 'Cyan'

$appDir = Split-Path -Parent (Resolve-Path $Path)
$generated = @{}

# A value counts as configured only if it is present AND is not one of the committed placeholders.
function Is-Real($value) {
    if ([string]::IsNullOrWhiteSpace($value)) { return $false }
    return -not ($value -like 'REPLACE*' -or $value -like '*REPLACE_ME*' -or $value -like '*PASTE_*')
}

# Reads a nested key such as Jwt:SigningKey out of an appsettings file, if that file exists.
function From-AppSettings($fileName, [string[]] $pathParts) {
    $file = Join-Path $appDir $fileName
    if (-not (Test-Path $file)) { return $null }
    try { $json = Get-Content $file -Raw | ConvertFrom-Json } catch { return $null }
    $node = $json
    foreach ($part in $pathParts) {
        if ($null -eq $node) { return $null }
        $node = $node.PSObject.Properties[$part].Value
    }
    return $node
}

function Ensure-Secret($envName, [string[]]$settingsPath, [scriptblock]$generator, $description) {
    $envs = Ensure-Child (Ensure-Child $server 'aspNetCore') 'environmentVariables'
    $existing = $envs.SelectSingleNode("environmentVariable[@name='$envName']")

    if ($existing -and (Is-Real $existing.value)) {
        Say "  $envName : already set in web.config" 'Green'
        return
    }

    foreach ($file in 'appsettings.Production.json', 'appsettings.json') {
        $value = From-AppSettings $file $settingsPath
        if (Is-Real $value) {
            Say "  $envName : already provided by $file" 'Green'
            return
        }
    }

    $value = & $generator
    if ($existing) { $existing.SetAttribute('value', $value) }
    else {
        $node = $xml.CreateElement('environmentVariable')
        $node.SetAttribute('name', $envName); $node.SetAttribute('value', $value)
        $envs.AppendChild($node) | Out-Null
    }
    $script:generated[$envName] = $value
    $script:changed = $true
    Say "  $envName : MISSING - generated a new one ($description)" 'Yellow'
}

Ensure-Secret 'Jwt__SigningKey' @('Jwt','SigningKey') {
    # 64 URL-safe characters, far above the 32 the API requires.
    $b = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
    [Convert]::ToBase64String($b).Replace('+','-').Replace('/','_').Replace('=','')
} 'signs access tokens'

Ensure-Secret 'Security__EncryptionKey' @('Security','EncryptionKey') {
    # Exactly 32 bytes, base64 - the form the protector prefers.
    $b = New-Object byte[] 32
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
    [Convert]::ToBase64String($b)
} 'encrypts the SendGrid key and order passwords'

if ($changed) {
    $backup = "$Path.bak-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Copy-Item $Path $backup
    Say "`n  backup  $backup" 'DarkGray'
    $xml.Save((Resolve-Path $Path))
    Say "  saved   $Path" 'Green'
} else { Say "`n  no change needed" 'Yellow' }

$envCount = @($server.SelectNodes('aspNetCore/environmentVariables/environmentVariable')).Count
Say "  environmentVariable entries now present: $envCount"

if ($generated.Count -gt 0) {
    Say "`n  +------------------------------------------------------------------+" 'Magenta'
    Say '  |  NEW SECRETS WERE GENERATED - COPY THEM SOMEWHERE SAFE NOW        |' 'Magenta'
    Say '  +------------------------------------------------------------------+' 'Magenta'
    foreach ($k in $generated.Keys) { Say "    $k = $($generated[$k])" 'Magenta' }
    Say ''
    if ($generated.ContainsKey('Jwt__SigningKey')) {
        Say '    Jwt__SigningKey: everyone signed in is signed out once. Expected.' 'DarkGray'
        Say '    It replaces the placeholder that is public in the repository.' 'DarkGray'
    }
    if ($generated.ContainsKey('Security__EncryptionKey')) {
        Say '    Security__EncryptionKey: back this up off the server. If it is ever lost or' 'DarkGray'
        Say '    changed, the stored SendGrid key must be re-entered and order passwords saved' 'DarkGray'
        Say '    under it can never be revealed.' 'DarkGray'
    }
}

if ($envCount -eq 0) {
    Say '  WARNING: still no environment variables. The API is running on the committed' 'Yellow'
    Say '  appsettings.json - check the connection string too. See templates\api-web.config.' 'Yellow'
}

Say "`n=== 2. IIS configuration (appcmd) ===" 'Cyan'
$appcmd = Join-Path $env:windir 'system32\inetsrv\appcmd.exe'
if (-not (Test-Path $appcmd)) {
    Say '  appcmd.exe not found - skipping (is this the IIS server?)' 'Yellow'
} elseif (-not $SiteName) {
    Say '  -SiteName not supplied - skipping. Re-run with -SiteName "<your api site>" if PUT stays 405.' 'Yellow'
    Say "  Sites on this server:" 'DarkGray'
    & $appcmd list site /text:name | ForEach-Object { Say "    $_" 'DarkGray' }
} else {
    foreach ($cmd in @(
        @('/section:system.webServer/modules',  "/-[name='WebDAVModule']"),
        @('/section:system.webServer/handlers', "/-[name='WebDAV']"))) {
        $out = & $appcmd set config "$SiteName" $cmd[0] $cmd[1] 2>&1
        if ($LASTEXITCODE -eq 0) { Say "  applied $($cmd[1]) to '$SiteName'" 'Green' }
        elseif ("$out" -match 'Cannot add duplicate|already present|unique') { Say "  $($cmd[1]) already present on '$SiteName'" }
        else { Say "  appcmd: $out" 'Yellow' }
    }
}

Say "`n=== 3. WebDAV Windows feature ===" 'Cyan'
# Get-WindowsOptionalFeature throws a terminating COMException when not elevated, and is absent
# on a machine without the DISM module, so both cases are caught rather than aborting the run.
$feature = $null
$featureError = $null
try { $feature = Get-WindowsOptionalFeature -Online -FeatureName IIS-WebDAV -ErrorAction Stop }
catch { $featureError = $_.Exception.Message.Trim() }

if ($featureError) {
    Say "  could not read the feature state: $featureError" 'Yellow'
    Say '  run from an ELEVATED PowerShell on the IIS server to inspect or remove it.' 'Yellow'
} elseif (-not $feature -or $feature.State -ne 'Enabled') {
    Say '  IIS-WebDAV is not enabled on this server (good).' 'Green'
} elseif ($DisableWebDavFeature) {
    Say '  removing the IIS-WebDAV feature...' 'Yellow'
    Disable-WindowsOptionalFeature -Online -FeatureName IIS-WebDAV -NoRestart | Out-Null
    Say '  removed. Nothing in this platform uses WebDAV.' 'Green'
    iisreset | Out-Null
    Say '  iisreset complete' 'Green'
} else {
    Say '  IIS-WebDAV is Enabled. Levels 1 and 2 usually suffice; if PUT is still 405,' 'Yellow'
    Say '  re-run this script adding  -DisableWebDavFeature' 'Yellow'
}

Say "`n=== 4. Restart ===" 'Cyan'
if ($AppPool) {
    Import-Module WebAdministration
    Restart-WebAppPool -Name $AppPool
    Say "  restarted app pool '$AppPool'" 'Green'
    Start-Sleep -Seconds 5
} else {
    Say '  -AppPool not supplied. Restart it yourself, then re-run to verify:' 'Yellow'
    Say '    Restart-WebAppPool -Name "<your api pool>"'
}

Say "`n=== 5. Verify ===" 'Cyan'
Say "  PUT $VerifyUrl  (no credentials sent - cannot change data)" 'DarkGray'
try {
    $r = Invoke-WebRequest -Uri $VerifyUrl -Method Put -UseBasicParsing -TimeoutSec 30
    Say "  HTTP $([int]$r.StatusCode) - the verb reaches the application. FIXED." 'Green'
} catch {
    if (-not $_.Exception.Response) { Say "  could not reach it: $($_.Exception.Message)" 'Yellow'; return }
    $status = [int]$_.Exception.Response.StatusCode
    $allow = $_.Exception.Response.Headers['Allow']
    if ($status -eq 405) {
        Say "  HTTP 405 (Allow: $allow) - STILL BLOCKED." 'Red'
        Say '  Next: re-run with  -SiteName "<your api site>" -DisableWebDavFeature' 'Yellow'
    } else {
        Say "  HTTP $status - the verb reaches the application. FIXED." 'Green'
        Say '  (401 or 400 here is correct: the request carried no token and no valid body.)' 'DarkGray'
    }
}
