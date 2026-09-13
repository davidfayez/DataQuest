# Build, Run & Deploy Guide

Two sections:

1. **[Local development](#section-1--build--run-locally)** — build and run the API (+ Swagger), the Admin panel and the Web (applicant) app on a developer machine.
2. **[IIS deployment](#section-2--publish-to-iis)** — publish all three to a Windows Server running IIS.

| Piece     | Project                            | Local URL                                            |
| --------- | ---------------------------------- | ---------------------------------------------------- |
| **API**   | `backend/src/DataVerification.API` | <http://localhost:5088> (Swagger at `/swagger`)      |
| **Web**   | `frontend/apps/web` (applicant)    | <http://localhost:5173>                              |
| **Admin** | `frontend/apps/admin` (back office)| <http://localhost:5174>                              |

Both SPAs call the API through a Vite dev proxy on `/api` targeting `http://localhost:5088`, which is
why the API must run on port **5088** in development.

---

# SECTION 1 — Build & run locally

## 1.1 Prerequisites

| Tool     | Version tested | Verify with          | Where to get it |
| -------- | -------------- | -------------------- | --------------- |
| .NET SDK | 10.0.100       | `dotnet --version`   | <https://dotnet.microsoft.com/download/dotnet/10.0> |
| Node.js  | 24.x (>=20)    | `node -v`            | <https://nodejs.org> |
| pnpm     | 11.x           | `pnpm -v`            | `corepack enable && corepack prepare pnpm@11 --activate` |
| Docker   | 28.x           | `docker -v`          | Optional — only for the local SQL Server + Mailpit containers |

Optional, only if you run migrations by hand:

```powershell
dotnet tool install --global dotnet-ef
```

The solution targets **net10.0** (`backend/Directory.Build.props`) and uses **central package
management** (`backend/Directory.Packages.props`) — package versions are pinned there, not in the
`.csproj` files.

---

## 1.2 Start the infrastructure (SQL Server + Mailpit)

From the repository root:

```powershell
docker compose up -d db mailpit
```

| Service | Endpoint | Credentials |
| --- | --- | --- |
| SQL Server 2022 | `localhost,1433` | `sa` / `Your_strong_Passw0rd` |
| Mailpit SMTP | `localhost:1025` | — |
| Mailpit web UI | <http://localhost:8025> | — |

Check they are healthy:

```powershell
docker compose ps
```

> **Skipping Docker?** If you already have a SQL Server instance on the machine (LocalDB, Express,
> or a full instance), you don't need the container — just point the connection string at it in the
> next step. Without Docker there is no Mailpit, but `Smtp:Enabled` is already `false` in
> development, so no mail is sent anyway.

---

## 1.3 ⚠️ Set the local connection string and JWT key (one time)

**Read this before the first `dotnet run`.**
`backend/src/DataVerification.API/appsettings.json` ships with `ConnectionStrings:DefaultConnection`
pointing at a **remote production database** (`Server=172.201.83.229;Database=VData_Prod`). In the
`Development` environment the API **migrates and seeds the database on boot**, including demo
applications. Override it locally *before* you start the API.

`DataVerification.API.csproj` has no `UserSecretsId` yet, so initialise the secret store first:

```powershell
cd backend\src\DataVerification.API
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=DataVerification;User Id=sa;Password=Your_strong_Passw0rd;TrustServerCertificate=True;MultipleActiveResultSets=true"
dotnet user-secrets set "Jwt:SigningKey" "<a random secret of at least 32 characters>"
```

For a machine-local SQL Server with Windows auth instead:

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost;Database=DataVerification;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
```

Generate a signing key quickly:

```powershell
[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
```

User secrets live outside the repo (`%APPDATA%\Microsoft\UserSecrets\`) and override
`appsettings.json`. `dotnet user-secrets init` adds a `UserSecretsId` element to the `.csproj` —
that element is safe to commit, it holds no secret.

Verify what is set:

```powershell
dotnet user-secrets list
```

> The API **refuses to start** if `Jwt:SigningKey` is missing or shorter than 32 characters
> (`Program.cs` throws `InvalidOperationException`). That is by design.

**Alternative — environment variables instead of user secrets** (nothing written to disk, per-shell):

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__DefaultConnection = "Server=localhost;Database=DataVerification;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
$env:Jwt__SigningKey = "<32+ character secret>"
```

The double underscore `__` is the .NET separator for nested configuration keys (`Jwt:SigningKey`).

---

## 1.4 Build the backend

```powershell
cd backend
dotnet restore
dotnet build                 # Debug, whole solution
dotnet build -c Release
```

---

## 1.5 Run the API + Swagger

```powershell
cd backend
dotnet run --project src\DataVerification.API --urls "http://localhost:5088"
```

`--urls` is **required**. `Properties/launchSettings.json` defaults to
`https://localhost:59441;http://localhost:59442`, but the Vite proxies and the API test suites all
expect **5088**. Permanent alternatives: set `ASPNETCORE_URLS=http://localhost:5088`, or edit
`applicationUrl` in `launchSettings.json` once.

Once it is up:

| What | URL |
| --- | --- |
| **Swagger UI** | <http://localhost:5088/swagger> |
| Swagger JSON | <http://localhost:5088/swagger/v1/swagger.json> |
| Health check | <http://localhost:5088/api/v1/health> → `200 OK` |

Quick smoke test from another terminal:

```powershell
curl.exe http://localhost:5088/api/v1/health
```

**What happens on first boot in Development:** the schema is migrated, lookups and the super admin
are seeded, and a set of demo applications is created (one per review status).

Seeded admin login: `admin@dataverification.local` / `Admin#12345`
(override with `Seed:SuperAdmin:Email` / `Seed:SuperAdmin:Password`).

### Using Swagger with authentication

1. Call `POST /api/v1/admin/auth/login` (or the applicant login) from Swagger.
2. Copy the `accessToken` from the response.
3. Click **Authorize** (top right), paste the token, confirm.
4. Protected endpoints now send `Authorization: Bearer <token>`.

> **Swagger is Development-only.** `Program.cs` registers `UseSwagger()` / `UseSwaggerUI()` inside
> `if (app.Environment.IsDevelopment())`. On a server it is off unless you change that — see
> [2.7](#27-optional-expose-swagger-on-the-server).

### Manual migrations (optional)

```powershell
cd backend
dotnet ef database update -p src\DataVerification.Infrastructure -s src\DataVerification.API
dotnet ef migrations add <Name> -p src\DataVerification.Infrastructure -s src\DataVerification.API
```

---

## 1.6 Install the frontend dependencies (one time)

The frontend is a **single pnpm workspace**. Always install from `frontend/`, never from an app
folder.

```powershell
cd frontend
pnpm install
```

Workspace members: `apps/web`, `apps/admin`, `packages/ui`, `packages/api-client`, `packages/i18n`.

---

## 1.7 Run Web and Admin

```powershell
cd frontend

pnpm dev          # both apps in parallel
pnpm dev:web      # applicant app only → http://localhost:5173
pnpm dev:admin    # admin panel only   → http://localhost:5174
```

Equivalent per-app form:

```powershell
pnpm --filter @dv/web dev
pnpm --filter @dv/admin dev
```

Both dev servers set `strictPort: true`, so they fail loudly instead of silently moving to another
port if 5173/5174 are taken. Both proxy `/api` → `http://localhost:5088`, so **no CORS configuration
is needed locally**.

### Pointing a SPA at a different API

Each app reads `VITE_API_BASE_URL`, defaulting to `/api/v1/` (the proxy). To hit a deployed API
directly, create `frontend/apps/web/.env.local` (or `apps/admin/.env.local`):

```
VITE_API_BASE_URL=https://api.example.com/api/v1/
```

That origin must also appear in the API's `Cors:AllowedOrigins`.

---

## 1.8 Build Web and Admin

```powershell
cd frontend

pnpm build                       # every workspace package + both apps
pnpm --filter @dv/web build      # applicant app → apps/web/dist
pnpm --filter @dv/admin build    # admin panel   → apps/admin/dist
```

`build` runs `tsc --noEmit && vite build`, so a type error fails the build.

Preview a production bundle locally:

```powershell
pnpm --filter @dv/web preview
pnpm --filter @dv/admin preview
```

---

## 1.9 Cold start, end to end

```powershell
# terminal 0 — infrastructure
docker compose up -d db mailpit

# terminal 1 — API (after setting user secrets, see 1.3)
cd backend
dotnet run --project src\DataVerification.API --urls "http://localhost:5088"

# terminal 2 — both front ends
cd frontend
pnpm install
pnpm dev
```

Then open:

- Applicant app → <http://localhost:5173>
- Admin panel → <http://localhost:5174> (`admin@dataverification.local` / `Admin#12345`)
- Swagger → <http://localhost:5088/swagger>
- Mailpit → <http://localhost:8025>

---

## 1.10 Quality gates

```powershell
cd backend  ; dotnet build ; dotnet test
cd frontend ; pnpm lint ; pnpm typecheck ; pnpm build
```

Full test layers:

```powershell
cd backend  ; dotnet test                              # unit + integration (needs SQL Server)
pwsh tests\api\run-all.ps1                             # API checks (API must be running on 5088)
cd frontend ; pnpm --filter @dv/i18n check:locales     # locale integrity
cd frontend ; pnpm --filter @dv/web e2e                # Playwright, applicant app
cd frontend ; pnpm --filter @dv/admin e2e              # Playwright, admin panel
```

First Playwright run only: `pnpm --filter @dv/web exec playwright install`.
Point integration tests at another database with `INTEGRATION_TESTS_CONNECTION`.
`tests\api\run-all.ps1` takes `-BaseUrl` if the API is not on `http://localhost:5088/api/v1`.

---

## 1.11 Local troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| SPA calls return 404 / `ECONNREFUSED` on `/api/v1/...` | API is not on 5088. Restart with `--urls "http://localhost:5088"`. |
| `Port 5173 is already in use` | `strictPort` is on — free the port or change it in `apps/web/vite.config.ts`. |
| API throws at boot: *"Jwt:SigningKey must be at least 32 characters"* | Set the secret — see [1.3](#13--set-the-local-connection-string-and-jwt-key-one-time). |
| API fails at boot with a SQL login/timeout error | Container not up (`docker compose ps`), or the connection string still points at the remote server. |
| Migrations ran against the wrong database | Development migrates **and seeds** on boot. Run `dotnet user-secrets list` before the first `dotnet run`. |
| No credentials email | `Smtp:Enabled` is `false` in development; credentials are echoed in the API response and log. Start Mailpit and set `Smtp:Enabled=true` to receive mail at <http://localhost:8025>. |
| Uploads rejected | Extension **and** magic bytes must match, 5 MB cap: `.pdf`, `.jpg`, `.jpeg`, `.png`. |
| `pnpm` resolution errors | Run `pnpm install` from `frontend/`, not from `apps/web` or `apps/admin`. |

---
---

# SECTION 2 — Publish to IIS

## 2.1 Target topology

Three IIS sites on one server, each with its own host header and HTTPS binding:

| Site | Content | Suggested binding | IIS app pool |
| --- | --- | --- | --- |
| **DataVerification.Api** | Published .NET output | `https://api.example.com` | `DataVerificationApi` — *No Managed Code* |
| **DataVerification.Web** | `apps/web/dist` (static) | `https://apply.example.com` | `DataVerificationStatic` — *No Managed Code* |
| **DataVerification.Admin** | `apps/admin/dist` (static) | `https://admin.example.com` | `DataVerificationStatic` — *No Managed Code* |

Suggested folder layout on the server:

```
E:\inetpub\DataVerification\
├── api\            <- published API (replaced on each deploy)
├── web\            <- apps/web/dist   contents
└── admin\          <- apps/admin/dist contents
E:\DataVerification\
├── storage\        <- uploaded files  (NEVER inside the site folder)
└── logs\           <- Serilog output  (optional, see 2.5)
```

> ### 🔴 HTTPS is mandatory in production
> `RefreshTokenCookie.cs` forces `Secure = true` and `SameSite = None` in **every environment except
> Development**. Browsers silently drop `Secure` cookies delivered over `http://`, so on a plain-HTTP
> deployment users can log in but their session **will not survive a page reload**. Bind a TLS
> certificate on all three sites before going live. (If you genuinely must run HTTP internally, that
> file has to be patched — there is no configuration switch for it.)

> ### About sub-path hosting
> Hosting a SPA under a virtual path (e.g. `https://example.com/admin`) needs **code changes** —
> `base: '/admin/'` in `apps/admin/vite.config.ts` **and** `basename` on `createBrowserRouter` in
> `apps/admin/src/app/routing/router.tsx` (neither is set today). Use separate host headers or
> separate ports instead; it is the supported path.

---

## 2.2 Server prerequisites

Install on the **Windows Server**, in this order:

### a. IIS role

Server Manager → *Add Roles and Features* → **Web Server (IIS)**, with:

- Web Server → Common HTTP Features → **Static Content**, **Default Document**, **HTTP Errors**
- Web Server → Health and Diagnostics → **HTTP Logging**
- Web Server → Performance → **Static Content Compression**, **Dynamic Content Compression**
- Web Server → Security → **Request Filtering**
- Management Tools → **IIS Management Console**

PowerShell equivalent (run elevated):

```powershell
Install-WindowsFeature -Name Web-Server,Web-Static-Content,Web-Default-Doc,Web-Http-Errors,Web-Http-Logging,Web-Stat-Compression,Web-Dyn-Compression,Web-Filtering,Web-Mgmt-Console -IncludeManagementTools
```

### b. ASP.NET Core 10 Hosting Bundle — required

Download from <https://dotnet.microsoft.com/download/dotnet/10.0> → *ASP.NET Core Runtime 10.x* →
**Hosting Bundle** (`dotnet-hosting-10.x.x-win.exe`). This installs the .NET runtime **and** the
ASP.NET Core Module V2 (ANCM) that lets IIS host the API.

Install it **after** IIS. If IIS was installed later, repair the bundle.

Then restart IIS:

```powershell
net stop was /y
net start w3svc
```

Verify:

```powershell
dotnet --list-runtimes          # expect Microsoft.AspNetCore.App 10.x
```

### c. URL Rewrite Module 2.1 — required for the SPAs

<https://www.iis.net/downloads/microsoft/url-rewrite>

Without it, the `web.config` files in [2.9](#29-configure-iis-for-the-spas) are invalid and the sites
return **500.19**. Deep links like `/applications/123` would 404 anyway.

### d. Verify

```powershell
Get-WindowsFeature Web-Server | Select-Object InstallState
Get-WebGlobalModule | Where-Object Name -like "*AspNetCore*"
Get-WebGlobalModule | Where-Object Name -like "*Rewrite*"
```

---

## 2.3 Prepare the database

Create an empty database and a SQL login on the target SQL Server:

```sql
CREATE DATABASE [DataVerification];
GO
CREATE LOGIN [dv_app] WITH PASSWORD = N'<strong password>';
GO
USE [DataVerification];
CREATE USER [dv_app] FOR LOGIN [dv_app];
ALTER ROLE db_owner ADD MEMBER [dv_app];   -- db_owner is needed only while migrations run
GO
```

### Applying migrations — pick one

**Option A (recommended): idempotent SQL script.** Generate it on the build machine, review it, run
it against the server with SSMS or `sqlcmd`. No migration code runs in production.

```powershell
cd backend
dotnet ef migrations script --idempotent `
  -p src\DataVerification.Infrastructure `
  -s src\DataVerification.API `
  -o ..\migrate.sql
```

```powershell
sqlcmd -S <server> -d DataVerification -U dv_app -P "<password>" -i migrate.sql
```

**Option B: migrate on startup.** Outside Development, migration and seeding are opt-in through
configuration. Set these on the IIS site for the **first** boot only, then remove them:

```
Database__MigrateOnStartup = true
Database__SeedOnStartup    = true
```

`SeedOnStartup` creates the lookup tables and the super admin. Demo applications are **never** seeded
outside Development, so production stays clean.

After the first successful boot, remove both variables and downgrade `dv_app` from `db_owner` to
`db_datareader` + `db_datawriter` + `EXECUTE`.

---

## 2.4 Build and publish the API

On the **build machine** (needs the .NET 10 SDK; the server only needs the Hosting Bundle):

```powershell
cd backend
dotnet restore
dotnet publish src\DataVerification.API\DataVerification.API.csproj `
  -c Release `
  -o E:\artifacts\api
```

This produces a self-contained-enough folder containing `DataVerification.API.dll`,
`DataVerification.API.exe`, all dependency DLLs, `appsettings.json`, and an auto-generated
**`web.config`** that wires up the ASP.NET Core Module:

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

Copy the folder to the server, e.g. `E:\inetpub\DataVerification\api`.

Robocopy is the safest transport — it lets you exclude runtime folders:

```powershell
robocopy E:\artifacts\api E:\inetpub\DataVerification\api /MIR /XD storage logs /R:2 /W:2
```

> `/MIR` deletes anything in the destination that is not in the source. The `/XD storage logs`
> exclusions keep uploaded files and log files from being wiped — but see [2.5](#25-application-pool-site-and-filesystem-permissions):
> putting `storage` outside the site folder entirely is better.

---

## 2.5 Application pool, site, and filesystem permissions

### a. Create the application pool

IIS Manager → **Application Pools** → *Add Application Pool*:

| Setting | Value |
| --- | --- |
| Name | `DataVerificationApi` |
| .NET CLR version | **No Managed Code** ← required; the API is not .NET Framework |
| Managed pipeline mode | Integrated |
| Identity | `ApplicationPoolIdentity` (default) |

Then *Advanced Settings*:

| Setting | Value | Why |
| --- | --- | --- |
| Load User Profile | `True` | Lets ASP.NET Core persist its Data Protection key ring |
| Start Mode | `AlwaysRunning` | Avoids a cold start on the first request |
| Idle Time-out (minutes) | `0` | Stops IIS from shutting the worker down when idle |
| Regular Time Interval (minutes) | `0` | Disables the default 29-hour recycle |

PowerShell equivalent (elevated):

```powershell
Import-Module WebAdministration
New-WebAppPool -Name "DataVerificationApi"
Set-ItemProperty IIS:\AppPools\DataVerificationApi -Name managedRuntimeVersion -Value ""
Set-ItemProperty IIS:\AppPools\DataVerificationApi -Name processModel.loadUserProfile -Value $true
Set-ItemProperty IIS:\AppPools\DataVerificationApi -Name startMode -Value "AlwaysRunning"
Set-ItemProperty IIS:\AppPools\DataVerificationApi -Name processModel.idleTimeout -Value "00:00:00"
Set-ItemProperty IIS:\AppPools\DataVerificationApi -Name recycling.periodicRestart.time -Value "00:00:00"
```

An empty `managedRuntimeVersion` **is** "No Managed Code".

### b. Create the site

IIS Manager → **Sites** → *Add Website*:

| Field | Value |
| --- | --- |
| Site name | `DataVerification.Api` |
| Application pool | `DataVerificationApi` |
| Physical path | `E:\inetpub\DataVerification\api` |
| Binding | `https`, port `443`, host name `api.example.com`, your TLS certificate |

```powershell
New-Website -Name "DataVerification.Api" `
  -PhysicalPath "E:\inetpub\DataVerification\api" `
  -ApplicationPool "DataVerificationApi" `
  -HostHeader "api.example.com" -Port 80
# then add the HTTPS binding with your certificate thumbprint:
New-WebBinding -Name "DataVerification.Api" -Protocol https -Port 443 -HostHeader "api.example.com" -SslFlags 1
```

### c. Create the runtime folders

Uploads resolve relative to the **application base directory** when `Storage:RootPath` is relative
(`LocalDiskFileStorage`), which would put user files inside the site folder — where a redeploy can
destroy them. Use an absolute path outside the site:

```powershell
New-Item -ItemType Directory -Force E:\DataVerification\storage
New-Item -ItemType Directory -Force E:\DataVerification\logs
New-Item -ItemType Directory -Force E:\inetpub\DataVerification\api\logs   # ANCM stdout log
```

### d. Grant permissions

The app pool identity is `IIS AppPool\DataVerificationApi`.

```powershell
# Read + execute on the application itself
icacls "E:\inetpub\DataVerification\api" /grant "IIS AppPool\DataVerificationApi:(OI)(CI)(RX)" /T

# Modify on uploads and logs
icacls "E:\DataVerification\storage" /grant "IIS AppPool\DataVerificationApi:(OI)(CI)(M)" /T
icacls "E:\DataVerification\logs"    /grant "IIS AppPool\DataVerificationApi:(OI)(CI)(M)" /T
icacls "E:\inetpub\DataVerification\api\logs" /grant "IIS AppPool\DataVerificationApi:(OI)(CI)(M)" /T
```

`(OI)(CI)` = inherit to files and subfolders. `/T` applies to existing children.

Without **Modify** on the storage and log paths the API throws on the first upload and Serilog's file
sink fails silently.

---

## 2.6 Configure the API on the server

Never ship secrets in `appsettings.json`. Put them in the site's `web.config` under
`<environmentVariables>` — ANCM injects them into the worker process. Edit
`E:\inetpub\DataVerification\api\web.config`:

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
          <environmentVariable name="Jwt__SigningKey" value="REPLACE_WITH_32_PLUS_CHARACTER_SECRET" />
          <environmentVariable name="Storage__RootPath" value="E:\DataVerification\storage" />
          <environmentVariable name="Cors__AllowedOrigins__0" value="https://apply.example.com" />
          <environmentVariable name="Cors__AllowedOrigins__1" value="https://admin.example.com" />
          <environmentVariable name="App__WebUrl" value="https://apply.example.com" />
          <environmentVariable name="App__AdminUrl" value="https://admin.example.com" />
          <environmentVariable name="SendGrid__ApiKey" value="REPLACE_ME" />
        </environmentVariables>
      </aspNetCore>
    </system.webServer>
  </location>
</configuration>
```

Notes on the settings above:

| Key | Why it matters |
| --- | --- |
| `Jwt__SigningKey` | **The API will not start** if this is missing or under 32 characters. |
| `Cors__AllowedOrigins__N` | Array elements are indexed from 0 and **replace nothing** — list every SPA origin. A missing origin makes every browser call fail CORS. |
| `Storage__RootPath` | Absolute path → uploads survive a redeploy. |
| `App__WebUrl` / `App__AdminUrl` | Used in outgoing emails and links. |
| `SendGrid__Enabled` | Already `true` in `appsettings.json`; supply a real `ApiKey` or set it to `false`. |
| `Database__MigrateOnStartup` | Only for the first boot if you chose Option B in [2.3](#23-prepare-the-database). Remove afterwards. |

**Serilog file logging (optional).** The default sink writes to `logs/dataverification-.log` relative
to the app folder. To redirect it outside the site, override the array element:

```xml
<environmentVariable name="Serilog__WriteTo__1__Args__path" value="E:\DataVerification\logs\dataverification-.log" />
```

Index `1` is the `File` sink (`0` is `Console`) as ordered in `appsettings.json`.

**Request size.** Uploads are capped at 5 MB by the domain, and Kestrel's limit is set in code. IIS's
own default (`maxAllowedContentLength`, ~28.6 MB) is already above that, so no change is needed. If
you tightened it at the server level, raise it back:

```xml
<security>
  <requestFiltering>
    <requestLimits maxAllowedContentLength="10485760" />
  </requestFiltering>
</security>
```

**Lock the file down** — it now contains secrets:

```powershell
icacls "E:\inetpub\DataVerification\api\web.config" /inheritance:r `
  /grant "Administrators:(F)" /grant "SYSTEM:(F)" /grant "IIS AppPool\DataVerificationApi:(R)"
```

Recycle the pool to apply:

```powershell
Restart-WebAppPool -Name "DataVerificationApi"
```

### Verify the API

```powershell
curl.exe -i https://api.example.com/api/v1/health
```

Expect `200 OK`. If not, jump to [2.12](#212-iis-troubleshooting).

---

## 2.7 (Optional) Expose Swagger on the server

`Program.cs` gates Swagger behind `app.Environment.IsDevelopment()`, so **Swagger is not available on
a Production IIS site**.

> ❌ **Do not** set `ASPNETCORE_ENVIRONMENT=Development` on the server to get it. Development also
> turns on migrate-on-boot, seed-on-boot, **demo application seeding**, and
> `App:EchoCredentialsInResponse` — which returns generated user credentials in API responses.

The supported way is a small code change: gate Swagger on configuration instead of the environment.

In `backend/src/DataVerification.API/Program.cs`, replace:

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

Rebuild, republish, and enable it per environment:

```xml
<environmentVariable name="Swagger__Enabled" value="true" />
```

Swagger then serves at `https://api.example.com/swagger`. If you enable it on a public host, restrict
it — IIS Manager → the site → **IP Address and Domain Restrictions** on the `/swagger` path, or a
`web.config` `<location path="swagger">` block with request filtering.

---

## 2.8 Build the SPAs for the server

Run this on the **build machine** (Node + pnpm; the IIS server needs neither).

Create the production env file for each app so the bundle points at the deployed API. `VITE_*`
variables are baked in at build time — they cannot be changed after the build.

`frontend/apps/web/.env.production`:

```
VITE_API_BASE_URL=https://api.example.com/api/v1/
```

`frontend/apps/admin/.env.production`:

```
VITE_API_BASE_URL=https://api.example.com/api/v1/
```

The trailing slash matters — the HTTP client joins paths onto this base.

Then build:

```powershell
cd frontend
pnpm install --frozen-lockfile
pnpm --filter @dv/web build      # → frontend\apps\web\dist
pnpm --filter @dv/admin build    # → frontend\apps\admin\dist
```

Copy the **contents** of each `dist` folder (not the folder itself) to the server:

```powershell
robocopy frontend\apps\web\dist   \\SERVER\E$\inetpub\DataVerification\web   /MIR
robocopy frontend\apps\admin\dist \\SERVER\E$\inetpub\DataVerification\admin /MIR
```

Every origin you used above must be listed in the API's `Cors__AllowedOrigins__N`
([2.6](#26-configure-the-api-on-the-server)).

---

## 2.9 Configure IIS for the SPAs

Both apps are client-side-routed SPAs: a request for `/applications/42` must return `index.html`, not
a 404. Create **`web.config` in the root of each site folder** (`web\` and `admin\`) with this
content — it is identical for both:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>

    <!-- Client-side routing: serve index.html for anything that is not a real file or folder -->
    <rewrite>
      <rules>
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
      <!-- IIS 404s on unknown extensions; these are all shipped by the Vite build -->
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
      <dynamicTypes>
        <add mimeType="application/json" enabled="true" />
      </dynamicTypes>
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

  <!-- Hashed asset filenames are immutable: cache them hard -->
  <location path="assets">
    <system.webServer>
      <staticContent>
        <clientCache cacheControlMode="UseMaxAge" cacheControlMaxAge="365.00:00:00" />
      </staticContent>
    </system.webServer>
  </location>

  <!-- index.html must never be cached, or users get stale bundles after a deploy -->
  <location path="index.html">
    <system.webServer>
      <staticContent>
        <clientCache cacheControlMode="DisableCache" />
      </staticContent>
    </system.webServer>
  </location>

</configuration>
```

### Create the two sites

Use a shared, static-only app pool (**No Managed Code** — these sites run no .NET code):

```powershell
Import-Module WebAdministration

New-WebAppPool -Name "DataVerificationStatic"
Set-ItemProperty IIS:\AppPools\DataVerificationStatic -Name managedRuntimeVersion -Value ""

New-Website -Name "DataVerification.Web" `
  -PhysicalPath "E:\inetpub\DataVerification\web" `
  -ApplicationPool "DataVerificationStatic" `
  -HostHeader "apply.example.com" -Port 80

New-Website -Name "DataVerification.Admin" `
  -PhysicalPath "E:\inetpub\DataVerification\admin" `
  -ApplicationPool "DataVerificationStatic" `
  -HostHeader "admin.example.com" -Port 80

# HTTPS bindings (SslFlags 1 = SNI); bind your certificate in IIS Manager or with netsh
New-WebBinding -Name "DataVerification.Web"   -Protocol https -Port 443 -HostHeader "apply.example.com" -SslFlags 1
New-WebBinding -Name "DataVerification.Admin" -Protocol https -Port 443 -HostHeader "admin.example.com" -SslFlags 1
```

Grant read access:

```powershell
icacls "E:\inetpub\DataVerification\web"   /grant "IIS AppPool\DataVerificationStatic:(OI)(CI)(RX)" /T
icacls "E:\inetpub\DataVerification\admin" /grant "IIS AppPool\DataVerificationStatic:(OI)(CI)(RX)" /T
```

---

## 2.10 Bind TLS certificates

Required — see the warning in [2.1](#21-target-topology).

1. Import the certificate: IIS Manager → server node → **Server Certificates** → *Import* (`.pfx`).
2. For each of the three sites: *Bindings…* → *Add* → type `https`, port `443`, host name, select the
   certificate, tick **Require Server Name Indication** if several sites share port 443.
3. Optionally add an HTTP→HTTPS redirect rule to each site's `web.config`, above the SPA fallback
   rule:

```xml
<rule name="HTTPS redirect" stopProcessing="true">
  <match url="(.*)" />
  <conditions>
    <add input="{HTTPS}" pattern="off" />
  </conditions>
  <action type="Redirect" url="https://{HTTP_HOST}/{R:1}" redirectType="Permanent" />
</rule>
```

The API already calls `UseForwardedHeaders()` for `X-Forwarded-For` / `X-Forwarded-Proto`, so a TLS
terminator in front of IIS works without further changes.

---

## 2.11 Post-deployment verification

Run through all of these before handing over:

```powershell
# 1. API process is alive
curl.exe -i https://api.example.com/api/v1/health          # 200 OK

# 2. Swagger, only if enabled in 2.7
curl.exe -i https://api.example.com/swagger/index.html

# 3. SPAs serve their shell
curl.exe -i https://apply.example.com/
curl.exe -i https://admin.example.com/

# 4. SPA deep link falls back to index.html (this is what the rewrite rule fixes)
curl.exe -i https://admin.example.com/applications/does-not-exist   # 200, HTML

# 5. CORS preflight is allowed for the admin origin
curl.exe -i -X OPTIONS https://api.example.com/api/v1/health `
  -H "Origin: https://admin.example.com" `
  -H "Access-Control-Request-Method: GET"                   # 204 + Access-Control-Allow-Origin
```

Then in a browser:

- [ ] Admin panel loads and you can sign in.
- [ ] **Reload the page after signing in — you stay signed in.** If you get logged out, the refresh
      cookie was rejected: check that the site is on HTTPS ([2.1](#21-target-topology)).
- [ ] Applicant flow: register, upload a `.pdf` under 5 MB, and confirm the file lands in
      `E:\DataVerification\storage\orders\...`.
- [ ] `E:\DataVerification\logs\dataverification-<date>.log` is being written.
- [ ] Emails send (or `SendGrid:Enabled` is deliberately `false`).

---

## 2.12 IIS troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| **HTTP 500.19** — config error | `web.config` is malformed, or **URL Rewrite is not installed** (the `<rewrite>` element is unrecognised). Install it ([2.2c](#c-url-rewrite-module-21--required-for-the-spas)). |
| **HTTP 500.30** — in-process start failure | The app threw during startup. Almost always `Jwt__SigningKey` missing/too short, or a SQL connection failure. Set `stdoutLogEnabled="true"` in `web.config`, retry, and read `logs\stdout_*.log`. Also check **Event Viewer → Windows Logs → Application**. |
| **HTTP 500.31 / 500.32** — cannot load runtime | Hosting Bundle missing or older than net10.0. Install it, then `net stop was /y && net start w3svc`. |
| **HTTP 502.5** — process failure | Same causes as 500.30 on out-of-process hosting. Read the stdout log. |
| **HTTP 503** — service unavailable | The app pool stopped. IIS Manager → Application Pools → check state; the Event Log will say why (usually a permissions error on the site folder). |
| **HTTP 404.0 on every SPA deep link** | The SPA fallback rewrite rule is missing or URL Rewrite is not installed. |
| **HTTP 405 on PUT/DELETE** | The WebDAV module is intercepting. Add to the API `web.config`: `<modules><remove name="WebDAVModule" /></modules>` and `<handlers><remove name="WebDAV" /></handlers>`. |
| Browser console: *blocked by CORS policy* | The SPA origin is not in `Cors__AllowedOrigins__N`. Add it and recycle the pool. Remember: `https://` and `http://`, and `www.` vs bare, are different origins. |
| Login works but **every reload logs you out** | The refresh cookie is `Secure; SameSite=None` outside Development and the browser dropped it. Serve all three sites over HTTPS. |
| API returns 401 for every call right after deploy | Clock skew is set to zero in `Program.cs`. Verify the server clock (`w32tm /query /status`). |
| Uploads fail with an access-denied error | App pool identity lacks **Modify** on `Storage__RootPath`. Re-run the `icacls` grant in [2.5d](#d-grant-permissions). |
| Uploads disappear after a deploy | `Storage:RootPath` is still relative, so files were written inside the site folder and `robocopy /MIR` removed them. Set the absolute path. |
| No log files at all | App pool identity lacks **Modify** on the log folder; Serilog fails the file sink silently. |
| Config change has no effect | ANCM reads `web.config` at process start. `Restart-WebAppPool -Name "DataVerificationApi"`. |
| Stale UI after redeploying a SPA | `index.html` was cached. Confirm the `<location path="index.html">` `DisableCache` block is present, and hard-refresh once. |
| API 404s on `/swagger` in production | Expected — Swagger is Development-gated. See [2.7](#27-optional-expose-swagger-on-the-server). |

---

## 2.13 Redeploy checklist

```powershell
# --- on the build machine ---
cd backend
dotnet publish src\DataVerification.API\DataVerification.API.csproj -c Release -o E:\artifacts\api

cd ..\frontend
pnpm install --frozen-lockfile
pnpm --filter @dv/web build
pnpm --filter @dv/admin build

# --- on the server (elevated) ---
Stop-WebAppPool -Name "DataVerificationApi"
# wait for the worker process to exit
robocopy E:\artifacts\api E:\inetpub\DataVerification\api /MIR /XD storage logs /XF web.config /R:2 /W:2
Start-WebAppPool -Name "DataVerificationApi"

robocopy <build>\frontend\apps\web\dist   E:\inetpub\DataVerification\web   /MIR /XF web.config
robocopy <build>\frontend\apps\admin\dist E:\inetpub\DataVerification\admin /MIR /XF web.config

curl.exe -i https://api.example.com/api/v1/health
```

`/XF web.config` preserves the server-specific configuration (secrets for the API, rewrite rules for
the SPAs) that is not part of the build output. Keep a copy of all three `web.config` files in a
secure location outside the repo — they are the only place the deployment configuration lives.

Apply any new EF migrations **before** starting the new API version — regenerate and run the
idempotent script from [2.3](#23-prepare-the-database).
