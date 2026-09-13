# IIS Deployment Guide — API, Web & Admin

Build and publish the Data Verification platform to a Windows Server running **IIS**, with **no
Docker** anywhere. Every step runs on the server itself, from source checkout to a verified,
working site.

## What you end up with

| Site | Content | Suggested binding | App pool |
| --- | --- | --- | --- |
| **DataVerification.Api** | Published .NET 10 output | `https://api.example.com` | `DataVerificationApi` — *No Managed Code* |
| **DataVerification.Web** | `apps/web/dist` (static applicant SPA) | `https://apply.example.com` | `DataVerificationStatic` — *No Managed Code* |
| **DataVerification.Admin** | `apps/admin/dist` (static back office SPA) | `https://admin.example.com` | `DataVerificationStatic` — *No Managed Code* |

Folder layout created by this guide:

```
E:\src\DataVerification\            <- source checkout (build only, never served by IIS)
E:\artifacts\
├── api\                            <- dotnet publish output
E:\inetpub\DataVerification\
├── api\                            <- API site      (replaced on each deploy)
├── web\                            <- applicant SPA (replaced on each deploy)
└── admin\                          <- admin SPA     (replaced on each deploy)
E:\DataVerification\
├── storage\                        <- uploaded files — outside the sites, survives redeploys
└── logs\                           <- Serilog output
```

Replace `E:` and the `example.com` host names with your own throughout.

> ### 🔴 Read this before you start: HTTPS is mandatory
> `backend/src/DataVerification.API/Infrastructure/RefreshTokenCookie.cs` forces
> `Secure = true` and `SameSite = None` on the refresh cookie in **every environment except
> Development**. Browsers silently discard `Secure` cookies delivered over `http://`, so on a
> plain-HTTP deployment users *can* sign in but their session **will not survive a page reload**.
> Have a TLS certificate ready — there is no configuration switch that changes this, only a code
> change.

> ### Offline server?
> Building on the server needs internet access for NuGet and the npm registry. If the server is
> isolated, do [Step 4](#step-4--build-and-publish-the-api) and [Step 5](#step-5--build-the-two-spas)
> on a machine that has access, copy `E:\artifacts\api` and the two `dist` folders across, and skip
> straight to [Step 6](#step-6--create-the-runtime-folders). Nothing else changes.

---

## Step 1 — Install the server prerequisites

Run everything below in an **elevated** PowerShell session.

### 1.1 IIS role and features

```powershell
Install-WindowsFeature -Name `
  Web-Server,Web-Static-Content,Web-Default-Doc,Web-Http-Errors,Web-Http-Logging,`
  Web-Stat-Compression,Web-Dyn-Compression,Web-Filtering,Web-Mgmt-Console `
  -IncludeManagementTools
```

Via the GUI instead: Server Manager → *Add Roles and Features* → **Web Server (IIS)**, and tick

- Common HTTP Features → **Static Content**, **Default Document**, **HTTP Errors**
- Health and Diagnostics → **HTTP Logging**
- Performance → **Static Content Compression**, **Dynamic Content Compression**
- Security → **Request Filtering**
- Management Tools → **IIS Management Console**

Confirm IIS answers:

```powershell
Get-Service W3SVC | Select-Object Status
curl.exe -I http://localhost/            # the IIS welcome page
```

### 1.2 ASP.NET Core 10 Hosting Bundle — required

This installs the .NET runtime **and** the ASP.NET Core Module V2 (ANCM), the piece that lets IIS
host the API. Install it **after** IIS; if IIS was added later, run the installer again and choose
*Repair*.

Download: <https://dotnet.microsoft.com/download/dotnet/10.0> → *ASP.NET Core Runtime 10.x* →
**Hosting Bundle** (`dotnet-hosting-10.x.x-win.exe`).

```powershell
Start-Process -Wait .\dotnet-hosting-10.0.x-win.exe -ArgumentList "/quiet","/norestart"
net stop was /y
net start w3svc
```

Verify — you must see `Microsoft.AspNetCore.App 10.x`:

```powershell
dotnet --list-runtimes
Get-WebGlobalModule | Where-Object Name -like "*AspNetCore*"
```

### 1.3 URL Rewrite Module 2.1 — required

Download: <https://www.iis.net/downloads/microsoft/url-rewrite>

Both SPAs need it for client-side routing. Without it the `web.config` files in
[Step 9](#step-9--configure-the-two-spa-sites) are rejected and the sites return **HTTP 500.19**.

```powershell
Get-WebGlobalModule | Where-Object Name -like "*Rewrite*"    # expect RewriteModule
```

### 1.4 .NET 10 SDK — required to build on the server

The Hosting Bundle only ships the *runtime*. Building needs the SDK.

Download: <https://dotnet.microsoft.com/download/dotnet/10.0> → **SDK 10.0.100** (x64 installer).

```powershell
dotnet --version                     # expect 10.0.100 or newer
```

### 1.5 Node.js and pnpm — required to build the SPAs

Download Node **24.x** (20+ works) from <https://nodejs.org>, then enable pnpm:

```powershell
corepack enable
corepack prepare pnpm@11 --activate

node -v      # v24.x
pnpm -v      # 11.x
```

IIS itself never runs Node — it only serves the static files produced by the build. If you build on
another machine, Node is not needed on the server at all.

### 1.6 SQL Server

Any reachable SQL Server 2019+ instance works: a local install, SQL Server Express on the same box,
or a remote server. Have ready:

- the server name / address (and instance name or port),
- an account that can create a database and a login.

If you need a free local instance: **SQL Server Express** —
<https://www.microsoft.com/sql-server/sql-server-downloads>. Install with Mixed Mode authentication
if you plan to use a SQL login, and enable the TCP/IP protocol in SQL Server Configuration Manager.

### 1.7 Git (optional)

Only if you will clone the repository on the server rather than copying a folder across.
<https://git-scm.com/download/win>

---

## Step 2 — Get the source onto the server

```powershell
New-Item -ItemType Directory -Force E:\src
cd E:\src
git clone <your-repository-url> DataVerification
cd E:\src\DataVerification
git checkout main
```

Or copy the repository folder to `E:\src\DataVerification` by any means you prefer.

You should now see `backend\`, `frontend\`, and `DataVerification.sln` under
`E:\src\DataVerification\backend`.

---

## Step 3 — Create the database

Connect to your SQL Server with SSMS or `sqlcmd` and run:

```sql
CREATE DATABASE [DataVerification];
GO

USE [master];
CREATE LOGIN [dv_app] WITH PASSWORD = N'<a strong password>';
GO

USE [DataVerification];
CREATE USER [dv_app] FOR LOGIN [dv_app];
ALTER ROLE db_owner ADD MEMBER [dv_app];   -- needed only while migrations run; downgrade after
GO
```

Resulting connection string (used in [Step 8](#step-8--configure-the-api)):

```
Server=SQLHOST;Database=DataVerification;User Id=dv_app;Password=<password>;TrustServerCertificate=True;MultipleActiveResultSets=true
```

**Windows authentication instead?** Only when SQL Server is on the *same* machine as IIS. Grant the
app pool identity access and use a trusted connection:

```sql
CREATE LOGIN [IIS APPPOOL\DataVerificationApi] FROM WINDOWS;
GO
USE [DataVerification];
CREATE USER [DataVerificationApi] FOR LOGIN [IIS APPPOOL\DataVerificationApi];
ALTER ROLE db_owner ADD MEMBER [DataVerificationApi];
GO
```

```
Server=localhost;Database=DataVerification;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true
```

The app pool named here is created in [Step 7](#step-7--create-the-application-pools); create the
login after that step if SQL Server rejects an unknown account.

### Apply the schema — pick one

**Option A (recommended) — idempotent SQL script.** No migration code ever runs in production, and
you get to review the DDL first.

```powershell
dotnet tool install --global dotnet-ef      # once per machine
cd E:\src\DataVerification\backend

dotnet ef migrations script --idempotent `
  -p src\DataVerification.Infrastructure `
  -s src\DataVerification.API `
  -o E:\artifacts\migrate.sql
```

`dotnet ef` builds the startup project, which reads configuration — if it complains about
`Jwt:SigningKey`, set it for that one command:

```powershell
$env:Jwt__SigningKey = "a temporary build-time value at least 32 chars long"
```

Then apply it:

```powershell
sqlcmd -S SQLHOST -d DataVerification -U dv_app -P "<password>" -i E:\artifacts\migrate.sql
```

**Option B — migrate on first boot.** Outside Development, migration and seeding are opt-in. Add
these two variables to the API's `web.config` in [Step 8](#step-8--configure-the-api) for the
**first** start, then delete them:

```
Database__MigrateOnStartup = true
Database__SeedOnStartup    = true
```

`SeedOnStartup` creates the lookup tables and the super admin account. Demo/sample applications are
seeded **only** in the Development environment, so a Production deployment stays clean either way.

After the schema exists, downgrade `dv_app` from `db_owner` to `db_datareader` + `db_datawriter`
plus `EXECUTE`.

---

## Step 4 — Build and publish the API

```powershell
cd E:\src\DataVerification\backend

dotnet restore
dotnet build -c Release                     # optional: fail fast on compile errors

dotnet publish src\DataVerification.API\DataVerification.API.csproj `
  -c Release `
  -o E:\artifacts\api
```

The solution targets **net10.0** (`backend\Directory.Build.props`) and uses central package
management (`backend\Directory.Packages.props`), so package versions come from there rather than the
individual `.csproj` files.

`E:\artifacts\api` now contains `DataVerification.API.exe`, `DataVerification.API.dll`, every
dependency, `appsettings.json`, and an auto-generated **`web.config`** wiring up ANCM:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath=".\DataVerification.API.exe"
                  stdoutLogEnabled="false"
                  stdoutLogFile=".\logs\stdout"
                  hostingModel="inprocess" />
    </system.webServer>
  </location>
</configuration>
```

You will extend that file with environment variables in [Step 8](#step-8--configure-the-api).

---

## Step 5 — Build the two SPAs

`VITE_*` variables are **compiled into the bundle** — they cannot be changed after the build, so the
API URL must be correct before you build.

Create `E:\src\DataVerification\frontend\apps\web\.env.production`:

```
VITE_API_BASE_URL=https://api.example.com/api/v1/
```

Create `E:\src\DataVerification\frontend\apps\admin\.env.production`:

```
VITE_API_BASE_URL=https://api.example.com/api/v1/
```

The **trailing slash is required** — the HTTP client appends paths onto this base.

The frontend is a single pnpm workspace. Install from `frontend\`, never from an app folder:

```powershell
cd E:\src\DataVerification\frontend

pnpm install --frozen-lockfile
pnpm --filter @dv/web build        # -> frontend\apps\web\dist
pnpm --filter @dv/admin build      # -> frontend\apps\admin\dist
```

`build` runs `tsc --noEmit && vite build`, so a TypeScript error fails the build.

> Every origin you point at the API must also be listed in the API's `Cors__AllowedOrigins__N`
> ([Step 8](#step-8--configure-the-api)), or the browser blocks every call.

> **Hosting a SPA under a sub-path** (e.g. `https://example.com/admin`) requires **code changes** —
> `base: '/admin/'` in `apps/admin/vite.config.ts` *and* `basename` on `createBrowserRouter` in
> `apps/admin/src/app/routing/router.tsx`. Neither is set today. Use separate host headers (or
> separate ports) as this guide does.

---

## Step 6 — Create the runtime folders

```powershell
New-Item -ItemType Directory -Force E:\inetpub\DataVerification\api
New-Item -ItemType Directory -Force E:\inetpub\DataVerification\web
New-Item -ItemType Directory -Force E:\inetpub\DataVerification\admin
New-Item -ItemType Directory -Force E:\inetpub\DataVerification\api\logs   # ANCM stdout log
New-Item -ItemType Directory -Force E:\DataVerification\storage            # uploads
New-Item -ItemType Directory -Force E:\DataVerification\logs               # Serilog
```

> **Why `storage` lives outside the site.** When `Storage:RootPath` is relative, uploads resolve
> against the application's base directory — i.e. *inside* the published site folder, where the next
> deploy destroys them. [Step 8](#step-8--configure-the-api) sets an absolute path instead.

Copy the build output into place:

```powershell
robocopy E:\artifacts\api E:\inetpub\DataVerification\api /MIR /XD storage logs /R:2 /W:2

robocopy E:\src\DataVerification\frontend\apps\web\dist   E:\inetpub\DataVerification\web   /MIR
robocopy E:\src\DataVerification\frontend\apps\admin\dist E:\inetpub\DataVerification\admin /MIR
```

Copy the **contents** of each `dist` folder, not the folder itself — `index.html` must sit at the
root of each site.

`/MIR` deletes anything in the destination that is absent from the source; `/XD storage logs` keeps
it from wiping those. Robocopy exit codes 0–7 are success, not failure.

---

## Step 7 — Create the application pools

Both pools run **No Managed Code** — the API is .NET 10 (not .NET Framework, which is all the CLR
version dropdown refers to), and the SPA sites run no server code at all.

```powershell
Import-Module WebAdministration

# --- API pool ---
New-WebAppPool -Name "DataVerificationApi"
Set-ItemProperty IIS:\AppPools\DataVerificationApi -Name managedRuntimeVersion -Value ""
Set-ItemProperty IIS:\AppPools\DataVerificationApi -Name processModel.loadUserProfile -Value $true
Set-ItemProperty IIS:\AppPools\DataVerificationApi -Name startMode -Value "AlwaysRunning"
Set-ItemProperty IIS:\AppPools\DataVerificationApi -Name processModel.idleTimeout -Value "00:00:00"
Set-ItemProperty IIS:\AppPools\DataVerificationApi -Name recycling.periodicRestart.time -Value "00:00:00"

# --- shared static pool for both SPAs ---
New-WebAppPool -Name "DataVerificationStatic"
Set-ItemProperty IIS:\AppPools\DataVerificationStatic -Name managedRuntimeVersion -Value ""
```

An **empty** `managedRuntimeVersion` *is* "No Managed Code".

Via the GUI: IIS Manager → **Application Pools** → *Add Application Pool*, then *Advanced Settings*:

| Setting | Value | Why |
| --- | --- | --- |
| .NET CLR version | **No Managed Code** | The API is not .NET Framework |
| Managed pipeline mode | Integrated | Default |
| Identity | `ApplicationPoolIdentity` | Default; the security principal used below |
| Load User Profile | `True` | Lets ASP.NET Core persist its Data Protection key ring |
| Start Mode | `AlwaysRunning` | No cold start on the first request |
| Idle Time-out (minutes) | `0` | IIS won't shut the worker down when idle |
| Regular Time Interval (minutes) | `0` | Disables the default 29-hour recycle |

---

## Step 8 — Configure the API

Secrets do not belong in `appsettings.json`. Put them in the site's `web.config` under
`<environmentVariables>` — ANCM injects them into the worker process at start.

> ⚠️ `backend\src\DataVerification.API\appsettings.json` ships with `ConnectionStrings:DefaultConnection`
> pointing at a **different, remote database** (`Server=172.201.83.229;Database=VData_Prod`). The
> override below is what stops your deployment from writing to it. Do not skip it.

Edit `E:\inetpub\DataVerification\api\web.config` to read:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <location path="." inheritInChildApplications="false">
    <system.webServer>
      <handlers>
        <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
      </handlers>
      <aspNetCore processPath=".\DataVerification.API.exe"
                  stdoutLogEnabled="false"
                  stdoutLogFile=".\logs\stdout"
                  hostingModel="inprocess">
        <environmentVariables>

          <environmentVariable name="ASPNETCORE_ENVIRONMENT" value="Production" />

          <environmentVariable name="ConnectionStrings__DefaultConnection"
                               value="Server=SQLHOST;Database=DataVerification;User Id=dv_app;Password=REPLACE_ME;TrustServerCertificate=True;MultipleActiveResultSets=true" />

          <environmentVariable name="Jwt__SigningKey" value="REPLACE_WITH_A_32_PLUS_CHARACTER_SECRET" />

          <environmentVariable name="Storage__RootPath" value="E:\DataVerification\storage" />

          <environmentVariable name="Cors__AllowedOrigins__0" value="https://apply.example.com" />
          <environmentVariable name="Cors__AllowedOrigins__1" value="https://admin.example.com" />

          <environmentVariable name="App__WebUrl"   value="https://apply.example.com" />
          <environmentVariable name="App__AdminUrl" value="https://admin.example.com" />

          <environmentVariable name="SendGrid__ApiKey" value="REPLACE_ME" />

          <!-- First boot only if you chose Option B in Step 3. Remove afterwards. -->
          <!-- <environmentVariable name="Database__MigrateOnStartup" value="true" /> -->
          <!-- <environmentVariable name="Database__SeedOnStartup"    value="true" /> -->

        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
```

The double underscore `__` is the .NET separator for nested configuration keys — `Jwt__SigningKey`
overrides `Jwt:SigningKey`.

| Key | Why it matters |
| --- | --- |
| `Jwt__SigningKey` | **The API refuses to start** if this is missing or shorter than 32 characters. |
| `Cors__AllowedOrigins__N` | Array elements are indexed from `0`. List **every** SPA origin — a missing one makes every browser call fail CORS. `https://` vs `http://`, and `www.` vs bare, are different origins. |
| `Storage__RootPath` | Absolute path → uploaded files survive a redeploy. |
| `App__WebUrl` / `App__AdminUrl` | Used to build links in outgoing email. |
| `SendGrid__ApiKey` | `SendGrid:Enabled` is already `true` in `appsettings.json` — supply a real key or add `SendGrid__Enabled = false`. |

Generate a signing key:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

**Serilog file output (optional).** The default sink writes to `logs\dataverification-.log` relative
to the application folder. To move it out of the site:

```xml
<environmentVariable name="Serilog__WriteTo__1__Args__path" value="E:\DataVerification\logs\dataverification-.log" />
```

Index `1` is the `File` sink; index `0` is `Console`, in the order they appear in `appsettings.json`.

**Request size.** Uploads are capped at 5 MB in the domain, and Kestrel's limit is set in code. IIS's
default `maxAllowedContentLength` (~28.6 MB) is already above that, so no change is needed unless you
tightened it server-wide:

```xml
<security>
  <requestFiltering>
    <requestLimits maxAllowedContentLength="10485760" />
  </requestFiltering>
</security>
```

**Lock the file down — it now holds secrets:**

```powershell
icacls "E:\inetpub\DataVerification\api\web.config" /inheritance:r `
  /grant "Administrators:(F)" /grant "SYSTEM:(F)" /grant "IIS AppPool\DataVerificationApi:(R)"
```

---

## Step 9 — Configure the two SPA sites

Both apps are client-side routed: a request for `/applications/42` must return `index.html`, not a
404. Create a **`web.config` in the root of each site folder** — `E:\inetpub\DataVerification\web`
and `E:\inetpub\DataVerification\admin`. The content is identical for both:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>

    <!-- Client-side routing: serve index.html for anything that is not a real file or folder -->
    <rewrite>
      <rules>
        <rule name="HTTPS redirect" stopProcessing="true">
          <match url="(.*)" />
          <conditions>
            <add input="{HTTPS}" pattern="off" />
          </conditions>
          <action type="Redirect" url="https://{HTTP_HOST}/{R:1}" redirectType="Permanent" />
        </rule>

        <rule name="SPA fallback" stopProcessing="true">
          <match url=".*" />
          <conditions logicalGrouping="MatchAll">
            <add input="{REQUEST_FILENAME}" matchType="IsFile" negate="true" />
            <add input="{REQUEST_FILENAME}" matchType="IsDirectory" negate="true" />
          </conditions>
          <action type="Rewrite" url="/index.html" />
        </rule>
      </rules>
    </rewrite>

    <staticContent>
      <!-- IIS 404s on extensions it does not know; these are all shipped by the Vite build -->
      <remove fileExtension=".json" />
      <mimeMap fileExtension=".json" mimeType="application/json" />
      <remove fileExtension=".woff2" />
      <mimeMap fileExtension=".woff2" mimeType="font/woff2" />
      <remove fileExtension=".woff" />
      <mimeMap fileExtension=".woff" mimeType="font/woff" />
      <remove fileExtension=".webmanifest" />
      <mimeMap fileExtension=".webmanifest" mimeType="application/manifest+json" />
      <remove fileExtension=".svg" />
      <mimeMap fileExtension=".svg" mimeType="image/svg+xml" />
    </staticContent>

    <httpCompression>
      <staticTypes>
        <add mimeType="application/javascript" enabled="true" />
        <add mimeType="text/css" enabled="true" />
        <add mimeType="image/svg+xml" enabled="true" />
      </staticTypes>
    </httpCompression>

    <defaultDocument>
      <files>
        <clear />
        <add value="index.html" />
      </files>
    </defaultDocument>

  </system.webServer>

  <!-- Vite gives assets content-hashed names, so they are immutable: cache them hard -->
  <location path="assets">
    <system.webServer>
      <staticContent>
        <clientCache cacheControlMode="UseMaxAge" cacheControlMaxAge="365.00:00:00" />
      </staticContent>
    </system.webServer>
  </location>

  <!-- index.html must never be cached, or users keep loading a stale bundle after a deploy -->
  <location path="index.html">
    <system.webServer>
      <staticContent>
        <clientCache cacheControlMode="DisableCache" />
      </staticContent>
    </system.webServer>
  </location>

</configuration>
```

Drop the `HTTPS redirect` rule if TLS is terminated by a load balancer in front of IIS — otherwise it
loops.

---

## Step 10 — Create the three sites

```powershell
Import-Module WebAdministration

New-Website -Name "DataVerification.Api" `
  -PhysicalPath "E:\inetpub\DataVerification\api" `
  -ApplicationPool "DataVerificationApi" `
  -HostHeader "api.example.com" -Port 80

New-Website -Name "DataVerification.Web" `
  -PhysicalPath "E:\inetpub\DataVerification\web" `
  -ApplicationPool "DataVerificationStatic" `
  -HostHeader "apply.example.com" -Port 80

New-Website -Name "DataVerification.Admin" `
  -PhysicalPath "E:\inetpub\DataVerification\admin" `
  -ApplicationPool "DataVerificationStatic" `
  -HostHeader "admin.example.com" -Port 80
```

Via the GUI: IIS Manager → **Sites** → *Add Website*, filling in site name, application pool,
physical path and binding for each row of the table at the top of this guide.

Make sure DNS resolves all three host names to this server. For a first test on the machine itself,
add them to `C:\Windows\System32\drivers\etc\hosts`:

```
127.0.0.1  api.example.com
127.0.0.1  apply.example.com
127.0.0.1  admin.example.com
```

### No host names available?

Use ports instead — one binding per site, e.g. API on `8080`, web on `8081`, admin on `8082`. Then
set `VITE_API_BASE_URL=https://server:8080/api/v1/` before building ([Step 5](#step-5--build-the-two-spas))
and list `https://server:8081` / `https://server:8082` in `Cors__AllowedOrigins__N`. Open the ports:

```powershell
New-NetFirewallRule -DisplayName "DataVerification IIS" -Direction Inbound `
  -Protocol TCP -LocalPort 8080,8081,8082 -Action Allow
```

---

## Step 11 — Grant filesystem permissions

The app pool identities are `IIS AppPool\DataVerificationApi` and
`IIS AppPool\DataVerificationStatic`.

```powershell
# API: read + execute on the application
icacls "E:\inetpub\DataVerification\api" /grant "IIS AppPool\DataVerificationApi:(OI)(CI)(RX)" /T

# API: modify on uploads and logs
icacls "E:\DataVerification\storage"          /grant "IIS AppPool\DataVerificationApi:(OI)(CI)(M)" /T
icacls "E:\DataVerification\logs"             /grant "IIS AppPool\DataVerificationApi:(OI)(CI)(M)" /T
icacls "E:\inetpub\DataVerification\api\logs" /grant "IIS AppPool\DataVerificationApi:(OI)(CI)(M)" /T

# SPAs: read + execute
icacls "E:\inetpub\DataVerification\web"   /grant "IIS AppPool\DataVerificationStatic:(OI)(CI)(RX)" /T
icacls "E:\inetpub\DataVerification\admin" /grant "IIS AppPool\DataVerificationStatic:(OI)(CI)(RX)" /T
```

`(OI)(CI)` makes the grant inherit to files and subfolders; `/T` applies it to items that already
exist.

Without **Modify** on the storage path the API throws on the first upload; without it on the log
paths, Serilog's file sink fails silently and you get no logs at all.

Re-run the `web.config` lock-down from [Step 8](#step-8--configure-the-api) if you ran these grants
afterwards — `/T` on the site folder re-grants read on that file, which is fine, but the
`/inheritance:r` restriction should be applied last.

---

## Step 12 — Bind TLS certificates

Required — see the warning at the top of this guide.

1. IIS Manager → server node → **Server Certificates** → *Import…* → select your `.pfx`.
2. For each of the three sites: right-click → *Edit Bindings…* → *Add* →
   - Type: `https`, Port: `443`
   - Host name: the site's host name
   - SSL certificate: the imported certificate
   - Tick **Require Server Name Indication** (several sites share port 443)

PowerShell equivalent:

```powershell
New-WebBinding -Name "DataVerification.Api"   -Protocol https -Port 443 -HostHeader "api.example.com"   -SslFlags 1
New-WebBinding -Name "DataVerification.Web"   -Protocol https -Port 443 -HostHeader "apply.example.com" -SslFlags 1
New-WebBinding -Name "DataVerification.Admin" -Protocol https -Port 443 -HostHeader "admin.example.com" -SslFlags 1

# attach the certificate (SNI) — repeat per host name
$cert = Get-ChildItem Cert:\LocalMachine\My | Where-Object Subject -like "*example.com*"
New-Item -Path "IIS:\SslBindings\!443!api.example.com" -Value $cert -SSLFlags 1
```

The API already calls `UseForwardedHeaders()` for `X-Forwarded-For` / `X-Forwarded-Proto`, so
terminating TLS on a load balancer in front of IIS works without further changes.

---

## Step 13 — Start everything and verify

```powershell
Restart-WebAppPool -Name "DataVerificationApi"
Restart-WebAppPool -Name "DataVerificationStatic"
Start-Website -Name "DataVerification.Api"
Start-Website -Name "DataVerification.Web"
Start-Website -Name "DataVerification.Admin"
```

Then work through every check:

```powershell
# 1. API process is alive
curl.exe -i https://api.example.com/api/v1/health                  # 200 OK

# 2. SPAs serve their shell
curl.exe -i https://apply.example.com/                             # 200, HTML
curl.exe -i https://admin.example.com/                             # 200, HTML

# 3. Deep link falls back to index.html — this is what the rewrite rule fixes
curl.exe -i https://admin.example.com/applications/does-not-exist  # 200, HTML (not 404)

# 4. CORS preflight is allowed for each SPA origin
curl.exe -i -X OPTIONS https://api.example.com/api/v1/health `
  -H "Origin: https://admin.example.com" `
  -H "Access-Control-Request-Method: GET"                          # 204 + Access-Control-Allow-Origin
```

In a browser:

- [ ] Admin panel loads and you can sign in.
- [ ] **Reload after signing in — you stay signed in.** Getting logged out means the refresh cookie
      was rejected; confirm the site is served over HTTPS.
- [ ] Applicant flow: register, upload a `.pdf` under 5 MB, and confirm the file appears under
      `E:\DataVerification\storage\orders\...`.
- [ ] `E:\DataVerification\logs\dataverification-<date>.log` is being written.
- [ ] Email sends, or `SendGrid__Enabled` is deliberately `false`.

If you used `Database__SeedOnStartup`, the super admin is `admin@dataverification.local` /
`Admin#12345` unless you overrode `Seed:SuperAdmin:Email` / `Seed:SuperAdmin:Password`.
**Change that password immediately after the first sign-in.**

Finally, if you enabled the first-boot migration variables in [Step 8](#step-8--configure-the-api),
remove them now and recycle the pool.

---

## Step 14 — (Optional) Enable Swagger on the server

`Program.cs` registers `UseSwagger()` / `UseSwaggerUI()` inside
`if (app.Environment.IsDevelopment())`, so **Swagger is not served on a Production site**. A 404 on
`/swagger` is expected behaviour, not a deployment fault.

> ❌ **Do not** set `ASPNETCORE_ENVIRONMENT=Development` on the server to get it. Development also
> switches on migrate-on-boot, seed-on-boot, **demo application seeding** (fabricated paid
> applications), and `App:EchoCredentialsInResponse`, which returns generated user credentials in API
> responses.

The supported route is a small code change — gate Swagger on configuration instead of environment.
In `backend\src\DataVerification.API\Program.cs`, replace:

```csharp
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

with:

```csharp
if (app.Environment.IsDevelopment()
    || app.Configuration.GetValue("Swagger:Enabled", false))
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
```

Rebuild and republish ([Step 4](#step-4--build-and-publish-the-api)), then enable it per server:

```xml
<environmentVariable name="Swagger__Enabled" value="true" />
```

Swagger UI is then at `https://api.example.com/swagger`, and the document at
`https://api.example.com/swagger/v1/swagger.json`.

To authenticate inside Swagger: call `POST /api/v1/admin/auth/login`, copy `accessToken` from the
response, click **Authorize**, paste it.

On a public host, restrict access — IIS Manager → the API site → **IP Address and Domain
Restrictions**, scoped to the `swagger` path.

---

## Step 15 — Redeploying a new version

```powershell
# 1. Pull and rebuild
cd E:\src\DataVerification
git pull

cd backend
dotnet publish src\DataVerification.API\DataVerification.API.csproj -c Release -o E:\artifacts\api

cd ..\frontend
pnpm install --frozen-lockfile
pnpm --filter @dv/web build
pnpm --filter @dv/admin build

# 2. Apply any new migrations BEFORE the new API starts
cd ..\backend
dotnet ef migrations script --idempotent -p src\DataVerification.Infrastructure -s src\DataVerification.API -o E:\artifacts\migrate.sql
sqlcmd -S SQLHOST -d DataVerification -U dv_app -P "<password>" -i E:\artifacts\migrate.sql

# 3. Swap the API
Stop-WebAppPool -Name "DataVerificationApi"
Start-Sleep -Seconds 5                       # let the worker process exit and release the DLLs
robocopy E:\artifacts\api E:\inetpub\DataVerification\api /MIR /XD storage logs /XF web.config /R:2 /W:2
Start-WebAppPool -Name "DataVerificationApi"

# 4. Swap the SPAs
robocopy E:\src\DataVerification\frontend\apps\web\dist   E:\inetpub\DataVerification\web   /MIR /XF web.config
robocopy E:\src\DataVerification\frontend\apps\admin\dist E:\inetpub\DataVerification\admin /MIR /XF web.config

# 5. Verify
curl.exe -i https://api.example.com/api/v1/health
```

`/XF web.config` preserves the server-specific configuration — secrets for the API, rewrite rules for
the SPAs — which is not part of the build output.

**Keep a backup of all three `web.config` files somewhere secure and outside the repository.** They
are the only place your deployment configuration exists; losing them means reconstructing every
secret by hand.

---

## Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| **500.19** — configuration error | Malformed `web.config`, or **URL Rewrite is not installed** so the `<rewrite>` element is unrecognised. See [Step 1.3](#13-url-rewrite-module-21--required). |
| **500.30** — in-process start failure | The app threw during startup. Almost always `Jwt__SigningKey` missing/under 32 characters, or the database is unreachable. Set `stdoutLogEnabled="true"` in the API `web.config`, retry, then read `E:\inetpub\DataVerification\api\logs\stdout_*.log`. Also check **Event Viewer → Windows Logs → Application**. |
| **500.31 / 500.32** — cannot load runtime | Hosting Bundle missing or older than net10.0. Install it, then `net stop was /y && net start w3svc`. |
| **502.5** — process failure | Same causes as 500.30. Read the stdout log. |
| **503** — service unavailable | The app pool stopped, usually on a permissions error. IIS Manager → Application Pools → check state; the Event Log gives the reason. |
| **404 on every SPA deep link** | SPA fallback rewrite rule missing, or URL Rewrite not installed. |
| **405 on PUT/DELETE** | The WebDAV module is intercepting. Add to the API `web.config` inside `<system.webServer>`: `<modules><remove name="WebDAVModule" /></modules>` and `<handlers><remove name="WebDAV" /></handlers>`. |
| Browser console: *blocked by CORS policy* | The SPA origin is not in `Cors__AllowedOrigins__N`. Add it and recycle the pool. Scheme, host and port must match exactly. |
| **Login works but every reload logs you out** | The refresh cookie is `Secure; SameSite=None` outside Development and the browser dropped it over HTTP. Serve all three sites over HTTPS ([Step 12](#step-12--bind-tls-certificates)). |
| Every API call returns 401 right after deploy | Token lifetime validation uses zero clock skew. Check the server clock: `w32tm /query /status`. |
| Uploads fail with access denied | App pool identity lacks **Modify** on `Storage__RootPath`. Re-run the grants in [Step 11](#step-11--grant-filesystem-permissions). |
| Uploads disappear after a deploy | `Storage:RootPath` is still relative, so files were written inside the site folder and `robocopy /MIR` removed them. Set the absolute path in [Step 8](#step-8--configure-the-api). |
| No log files at all | App pool identity lacks **Modify** on the log folder; Serilog's file sink fails silently. |
| Configuration change has no effect | ANCM reads `web.config` only at process start. `Restart-WebAppPool -Name "DataVerificationApi"`. |
| Stale UI after redeploying a SPA | `index.html` was cached. Confirm the `<location path="index.html">` `DisableCache` block is present, then hard-refresh once. |
| `/swagger` returns 404 | Expected on a Production site. See [Step 14](#step-14--optional-enable-swagger-on-the-server). |
| `dotnet publish` fails with a file-in-use error | The old worker process still holds the DLLs. `Stop-WebAppPool` and wait a few seconds before copying. |
| `pnpm install` resolution errors | Run it from `frontend\`, not from `apps\web` or `apps\admin` — it is one workspace. |
| SQL login fails for `IIS APPPOOL\...` | Windows authentication only works when SQL Server is on the same machine. Use a SQL login for a remote server. |
