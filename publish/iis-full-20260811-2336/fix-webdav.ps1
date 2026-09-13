<#
    Unblocks PUT and DELETE on the API site by removing IIS's WebDAV module and handler.

    Patches the web.config that is ALREADY on the server, in place. Everything else in the file —
    especially <environmentVariables>, which holds the connection string, Jwt__SigningKey and
    Security__EncryptionKey — is left exactly as it is. A timestamped backup is written first.

    Safe to run more than once: if the fix is already present it reports that and changes nothing.

    Run in an elevated PowerShell on the IIS server:

        .\fix-webdav.ps1 -AppPool "<your api pool>"

    Omit -AppPool to patch the file without restarting; the change takes effect on the next recycle.
    Use -Path if the site is not at the default location.
#>
[CmdletBinding()]
param(
    [string] $Path = 'E:\inetpub\DataVerification\api\web.config',
    [string] $AppPool
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path $Path)) { throw "No web.config at '$Path'. Pass -Path with the correct location." }

Write-Host "Patching $Path" -ForegroundColor Cyan

$xml = New-Object System.Xml.XmlDocument
$xml.PreserveWhitespace = $true
$xml.Load((Resolve-Path $Path))

# The ASP.NET Core template nests everything under <location path="."><system.webServer>, but a
# hand-written file may put system.webServer at the root. Handle both.
$server = $xml.SelectSingleNode('/configuration/location/system.webServer')
if (-not $server) { $server = $xml.SelectSingleNode('/configuration/system.webServer') }
if (-not $server) { throw 'No <system.webServer> section found. Is this the API site web.config?' }

function Ensure-Child($parent, $name) {
    $node = $parent.SelectSingleNode($name)
    if (-not $node) {
        $node = $xml.CreateElement($name)
        $parent.AppendChild($node) | Out-Null
        Write-Host "  created <$name>"
    }
    return $node
}

$changed = $false

# --- handlers: <remove name="WebDAV" /> must come BEFORE the aspNetCore handler ---------------
$handlers = Ensure-Child $server 'handlers'
$removeWebDav = $handlers.SelectSingleNode("remove[@name='WebDAV']")
if (-not $removeWebDav) {
    $removeWebDav = $xml.CreateElement('remove')
    $removeWebDav.SetAttribute('name', 'WebDAV')
    if ($handlers.FirstChild) { $handlers.InsertBefore($removeWebDav, $handlers.FirstChild) | Out-Null }
    else { $handlers.AppendChild($removeWebDav) | Out-Null }
    Write-Host "  added   <handlers><remove name=""WebDAV"" />" -ForegroundColor Green
    $changed = $true
} else {
    # Present but possibly after the aspNetCore handler, which does not work. Force it to the front.
    # Reference equality: XmlElement in .NET Framework has no IsSameNode.
    if ($handlers.FirstChild -and -not [object]::ReferenceEquals($handlers.FirstChild, $removeWebDav)) {
        $handlers.RemoveChild($removeWebDav) | Out-Null
        $handlers.InsertBefore($removeWebDav, $handlers.FirstChild) | Out-Null
        Write-Host "  moved   <remove name=""WebDAV"" /> ahead of the aspNetCore handler" -ForegroundColor Green
        $changed = $true
    } else {
        Write-Host "  present <handlers><remove name=""WebDAV"" />"
    }
}

# The site is useless without this handler, so add it if the file somehow lacks it.
if (-not $handlers.SelectSingleNode("add[@name='aspNetCore']")) {
    $add = $xml.CreateElement('add')
    $add.SetAttribute('name', 'aspNetCore')
    $add.SetAttribute('path', '*')
    $add.SetAttribute('verb', '*')
    $add.SetAttribute('modules', 'AspNetCoreModuleV2')
    $add.SetAttribute('resourceType', 'Unspecified')
    $handlers.AppendChild($add) | Out-Null
    Write-Host "  added   <add name=""aspNetCore"" ... />" -ForegroundColor Green
    $changed = $true
}

# --- modules: <remove name="WebDAVModule" /> ---------------------------------------------------
$modules = Ensure-Child $server 'modules'
if (-not $modules.SelectSingleNode("remove[@name='WebDAVModule']")) {
    $rm = $xml.CreateElement('remove')
    $rm.SetAttribute('name', 'WebDAVModule')
    $modules.AppendChild($rm) | Out-Null
    Write-Host "  added   <modules><remove name=""WebDAVModule"" />" -ForegroundColor Green
    $changed = $true
} else {
    Write-Host "  present <modules><remove name=""WebDAVModule"" />"
}

if (-not $changed) {
    Write-Host "`nAlready patched - no change made." -ForegroundColor Yellow
} else {
    $backup = "$Path.bak-$(Get-Date -Format 'yyyyMMdd-HHmmss')"
    Copy-Item $Path $backup
    Write-Host "`nBackup written: $backup" -ForegroundColor DarkGray
    $xml.Save((Resolve-Path $Path))
    Write-Host "Saved $Path" -ForegroundColor Green
}

# Confirm the environment variables survived - this is the part that must never be lost.
$envCount = @($server.SelectNodes('aspNetCore/environmentVariables/environmentVariable')).Count
Write-Host "environmentVariable entries still present: $envCount"
if ($envCount -eq 0) {
    Write-Host "  WARNING: this web.config declares no environment variables." -ForegroundColor Yellow
    Write-Host "  The API is then running on the committed appsettings.json, which includes a" -ForegroundColor Yellow
    Write-Host "  PUBLIC placeholder Jwt:SigningKey. See templates\api-web.config." -ForegroundColor Yellow
}

if ($AppPool) {
    Import-Module WebAdministration
    Restart-WebAppPool -Name $AppPool
    Write-Host "Restarted app pool '$AppPool'." -ForegroundColor Green
} else {
    Write-Host "`nNo -AppPool given. Restart it yourself for the change to take effect:" -ForegroundColor Yellow
    Write-Host "  Restart-WebAppPool -Name ""<your api pool>"""
}

Write-Host "`nThen confirm (401 = fixed, 405 = still blocked):" -ForegroundColor Cyan
Write-Host "  try { Invoke-WebRequest -Uri 'https://vdataapi.nen-global.org/api/v1/orders/setup' -Method Put -UseBasicParsing }"
Write-Host "  catch { ""PUT now returns HTTP `$([int]`$_.Exception.Response.StatusCode)"" }"
