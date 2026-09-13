# Publish Web & Admin to IIS — Step by Step

A short, practical guide to putting the two front-end apps on an IIS server. The API is **already
deployed** at <https://vdataapi.nen-global.org> — this guide only covers the two SPAs.

| App | Source folder | Build output | What it is |
| --- | --- | --- | --- |
| **Web** | `frontend/apps/web` | `apps/web/dist` | Applicant site |
| **Admin** | `frontend/apps/admin` | `apps/admin/dist` | Back office |

Both are **static sites** — plain HTML, JS and CSS. IIS just serves files; there is no .NET, no Node
and no application pool code involved. The whole job is: build → copy → add a `web.config` → create
a site.

**Suggested host names** (matching your existing `vdataapi.nen-global.org`):

| App | Host name |
| --- | --- |
| Web | `vdata.nen-global.org` |
| Admin | `vdataadmin.nen-global.org` |

> ### 🔴 Two things that will break login if you skip them
> 1. **Both sites must be HTTPS.** The API sends its refresh cookie as `Secure`. Browsers throw away
>    `Secure` cookies that arrive over `http://`, so on a plain-HTTP site users can sign in but get
>    signed out on every page refresh.
> 2. **The API must allow your new origins in CORS.** Until that is done, every API call from the
>    SPAs is blocked by the browser. See [Step 8](#step-8--allow-the-new-origins-on-the-api).
>
> Staying on a `nen-global.org` subdomain (as suggested above) also keeps the refresh cookie
> *same-site* with the API, so browsers that block third-party cookies won't interfere.

---

## Step 1 — What you need

**On the machine where you build** (your PC is fine — the server does not need these):

| Tool | Version | Check | Get it |
| --- | --- | --- | --- |
| Node.js | 20 or newer (24 tested) | `node -v` | <https://nodejs.org> |
| pnpm | 11.x | `pnpm -v` | `corepack enable` then `corepack prepare pnpm@11 --activate` |

**On the IIS server:**

| Requirement | Notes |
| --- | --- |
| IIS with **Static Content** | Server Manager → Add Roles and Features → Web Server (IIS) |
| **URL Rewrite Module 2.1** | Download: <https://www.iis.net/downloads/microsoft/url-rewrite> — **required**, see below |
| A TLS certificate | For the HTTPS bindings in [Step 7](#step-7--add-https) |

> **Why URL Rewrite is mandatory.** These apps handle their own routing in the browser. When a user
> opens `https://vdataadmin.nen-global.org/applications/123` directly, IIS looks for a folder called
> `applications` on disk, doesn't find it, and returns 404. The rewrite rule in
> [Step 5](#step-5--add-the-webconfig-file) tells IIS to return `index.html` instead and let the app
> handle the route. Without the module installed, the `web.config` is also rejected outright and the
> site returns **HTTP 500.19**.

Quick check on the server (PowerShell):

```powershell
Get-WebGlobalModule | Where-Object Name -like "*Rewrite*"     # expect: RewriteModule
```

---

## Step 2 — Point both apps at the API

The API address is **compiled into** the JavaScript bundle. It cannot be changed after building, so
this comes first.

Create these two files (they don't exist yet):

**`frontend/apps/web/.env.production`**

```
VITE_API_BASE_URL=https://vdataapi.nen-global.org/api/v1/
```

**`frontend/apps/admin/.env.production`**

```
VITE_API_BASE_URL=https://vdataapi.nen-global.org/api/v1/
```

Both files hold the same line. Watch three things:

- `https://`, not `http://`
- the `/api/v1/` path — confirmed live at <https://vdataapi.nen-global.org/swagger/index.html>
- **the trailing slash** — the app appends paths onto this value, so `.../api/v1` without the slash
  produces broken URLs

---

## Step 3 — Build the two apps

Open PowerShell in the **`frontend`** folder. Not `apps/web`, not `apps/admin` — this is one
workspace and installing from a sub-folder fails.

```powershell
cd "e:\Data Verification\DataVerification\frontend"

pnpm install

pnpm --filter @dv/web build
pnpm --filter @dv/admin build
```

The build type-checks first, so a TypeScript error stops it. When both finish you have:

```
frontend\apps\web\dist\      <- index.html, assets\, ...
frontend\apps\admin\dist\    <- index.html, assets\, ...
```

Confirm `index.html` exists in each:

```powershell
Get-ChildItem apps\web\dist, apps\admin\dist
```

> **Prefer not to keep the `.env.production` files?** Set the variable inline instead:
> ```powershell
> $env:VITE_API_BASE_URL = "https://vdataapi.nen-global.org/api/v1/"
> pnpm --filter @dv/web build
> pnpm --filter @dv/admin build
> ```

---

## Step 4 — Copy the files to the server

Create two folders on the server:

```
E:\inetpub\vdata\web
E:\inetpub\vdata\admin
```

```powershell
New-Item -ItemType Directory -Force E:\inetpub\vdata\web
New-Item -ItemType Directory -Force E:\inetpub\vdata\admin
```

Copy the **contents** of each `dist` folder into them — `index.html` must sit directly in
`E:\inetpub\vdata\web`, **not** in `E:\inetpub\vdata\web\dist`.

Over a network share:

```powershell
robocopy "e:\Data Verification\DataVerification\frontend\apps\web\dist"   \\SERVER\E$\inetpub\vdata\web   /MIR
robocopy "e:\Data Verification\DataVerification\frontend\apps\admin\dist" \\SERVER\E$\inetpub\vdata\admin /MIR
```

Copying by hand (RDP, USB, zip) works just as well — drag the *contents* of `dist`, not the folder.

`/MIR` mirrors: it also deletes files in the destination that no longer exist in the build, which is
what you want so old bundles don't pile up.

---

## Step 5 — Add the `web.config` file

Create a file named **`web.config`** in `E:\inetpub\vdata\web`, and **an identical copy** in
`E:\inetpub\vdata\admin`. Same content for both:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <system.webServer>

    <rewrite>
      <rules>
        <!-- Send every http:// visitor to https:// -->
        <rule name="HTTPS redirect" stopProcessing="true">
          <match url="(.*)" />
          <conditions>
            <add input="{HTTPS}" pattern="off" />
          </conditions>
          <action type="Redirect" url="https://{HTTP_HOST}/{R:1}" redirectType="Permanent" />
        </rule>

        <!-- If the request is not a real file or folder, serve index.html and let the app route -->
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
      <!-- IIS returns 404 for file types it does not recognise; the build ships all of these -->
      <remove fileExtension=".json" />
      <mimeMap fileExtension=".json" mimeType="application/json" />
      <remove fileExtension=".woff2" />
      <mimeMap fileExtension=".woff2" mimeType="font/woff2" />
      <remove fileExtension=".woff" />
      <mimeMap fileExtension=".woff" mimeType="font/woff" />
      <remove fileExtension=".svg" />
      <mimeMap fileExtension=".svg" mimeType="image/svg+xml" />
      <remove fileExtension=".webmanifest" />
      <mimeMap fileExtension=".webmanifest" mimeType="application/manifest+json" />
    </staticContent>

    <defaultDocument>
      <files>
        <clear />
        <add value="index.html" />
      </files>
    </defaultDocument>

  </system.webServer>

  <!-- Files in assets\ have a content hash in their name, so they never change: cache for a year -->
  <location path="assets">
    <system.webServer>
      <staticContent>
        <clientCache cacheControlMode="UseMaxAge" cacheControlMaxAge="365.00:00:00" />
      </staticContent>
    </system.webServer>
  </location>

  <!-- index.html must never be cached, or users keep seeing the old version after an update -->
  <location path="index.html">
    <system.webServer>
      <staticContent>
        <clientCache cacheControlMode="DisableCache" />
      </staticContent>
    </system.webServer>
  </location>

</configuration>
```

> If TLS is terminated by a load balancer or reverse proxy in front of IIS, **delete the
> `HTTPS redirect` rule** — IIS will never see `{HTTPS}` as `on` and the site will redirect in a
> loop.

Save it as `web.config` exactly — not `web.config.txt`. In Notepad choose *Save as type: All Files*.

---

## Step 6 — Create the two sites in IIS

Open **IIS Manager** on the server.

### 6a. One application pool for both

1. Right-click **Application Pools** → *Add Application Pool…*
2. Name: `vdataStatic`
3. **.NET CLR version: `No Managed Code`** ← these sites run no server-side code
4. Managed pipeline mode: `Integrated`
5. OK

### 6b. The Web site

1. Right-click **Sites** → *Add Website…*
2. Site name: `vdata.web`
3. Application pool: `vdataStatic` (click *Select…*)
4. Physical path: `E:\inetpub\vdata\web`
5. Binding: type `http`, port `80`, host name `vdata.nen-global.org`
6. OK

### 6c. The Admin site

Repeat with:

- Site name: `vdata.admin`
- Physical path: `E:\inetpub\vdata\admin`
- Host name: `vdataadmin.nen-global.org`

### 6d. Give IIS permission to read the files

Open PowerShell **as Administrator**:

```powershell
icacls "E:\inetpub\vdata\web"   /grant "IIS AppPool\vdataStatic:(OI)(CI)(RX)" /T
icacls "E:\inetpub\vdata\admin" /grant "IIS AppPool\vdataStatic:(OI)(CI)(RX)" /T
```

### Prefer PowerShell for the whole step?

```powershell
Import-Module WebAdministration

New-WebAppPool -Name "vdataStatic"
Set-ItemProperty IIS:\AppPools\vdataStatic -Name managedRuntimeVersion -Value ""   # = No Managed Code

New-Website -Name "vdata.web" `
  -PhysicalPath "E:\inetpub\vdata\web" `
  -ApplicationPool "vdataStatic" `
  -HostHeader "vdata.nen-global.org" -Port 80

New-Website -Name "vdata.admin" `
  -PhysicalPath "E:\inetpub\vdata\admin" `
  -ApplicationPool "vdataStatic" `
  -HostHeader "vdataadmin.nen-global.org" -Port 80
```

### DNS

Point both host names at the server's public IP with an `A` record (or a `CNAME`). To test before
DNS is ready, add them to `C:\Windows\System32\drivers\etc\hosts` on your own PC:

```
<server-ip>  vdata.nen-global.org
<server-ip>  vdataadmin.nen-global.org
```

---

## Step 7 — Add HTTPS

Not optional — see the warning at the top.

1. IIS Manager → click the **server name** (top of the left tree) → **Server Certificates**
2. *Import…* → select your `.pfx` → enter the password → OK
3. Click **Sites** → `vdata.web` → *Bindings…* (right panel) → *Add…*
   - Type: `https`
   - Port: `443`
   - Host name: `vdata.nen-global.org`
   - ✅ **Require Server Name Indication**
   - SSL certificate: the one you imported
   - OK
4. Repeat for `vdata.admin` with host name `vdataadmin.nen-global.org`

Open port 443 if the firewall is closed:

```powershell
New-NetFirewallRule -DisplayName "HTTPS" -Direction Inbound -Protocol TCP -LocalPort 443 -Action Allow
```

---

## Step 8 — Allow the new origins on the API

**Do not skip this.** The API only accepts browser calls from origins on its allow-list. Until your
two host names are added, every request from the SPAs fails with a CORS error and the sites look
completely broken.

On the **API server** (`vdataapi.nen-global.org`), add these to the API's configuration —
in its `web.config` under `<aspNetCore><environmentVariables>` if it runs on IIS:

```xml
<environmentVariable name="Cors__AllowedOrigins__0" value="https://vdata.nen-global.org" />
<environmentVariable name="Cors__AllowedOrigins__1" value="https://vdataadmin.nen-global.org" />
<!-- Keep these if the SPAs are still temporarily on Vercel -->
<environmentVariable name="Cors__AllowedOrigins__2" value="https://data-verification.vercel.app" />
<environmentVariable name="Cors__AllowedOrigins__3" value="https://data-verification-admin.vercel.app" />
<environmentVariable name="App__WebUrl"   value="https://vdata.nen-global.org" />
<environmentVariable name="App__AdminUrl" value="https://vdataadmin.nen-global.org" />
```

Then restart the API's application pool.

The origin must match **exactly** — scheme, host and port. `https://vdata.nen-global.org` and
`http://vdata.nen-global.org` are different origins, as are `vdata.` and `www.vdata.`.

`App__WebUrl` / `App__AdminUrl` are used to build the links inside emails the system sends, so they
should point at the same addresses.

---

## Step 9 — Check that it works

In a browser:

1. Open <https://vdata.nen-global.org> — the applicant site loads.
2. Open <https://vdataadmin.nen-global.org> — the admin sign-in page loads.
3. Press **F12** → **Console** tab. There should be no red CORS errors.
   *If you see "blocked by CORS policy" → Step 8 is incomplete or the pool wasn't restarted.*
4. Sign in to the admin panel.
5. **Refresh the page (F5). You must stay signed in.**
   *If you get kicked back to the login page → the site isn't on HTTPS, or you opened it over
   `http://`.*
6. Navigate to any inner page, then press F5 there.
   *If you get a 404 → the `web.config` is missing, or URL Rewrite isn't installed.*

From PowerShell:

```powershell
# both sites answer
curl.exe -I https://vdata.nen-global.org/
curl.exe -I https://vdataadmin.nen-global.org/

# deep link returns the app, not a 404 — this is what the rewrite rule fixes
curl.exe -I https://vdataadmin.nen-global.org/applications/anything
```

All three should return `200 OK`.

---

## Step 10 — Publishing an update later

Every update is the same three commands. Nothing in IIS needs touching again.

```powershell
# 1. Rebuild
cd "e:\Data Verification\DataVerification\frontend"
pnpm install
pnpm --filter @dv/web build
pnpm --filter @dv/admin build

# 2. Copy over — /XF web.config keeps the file you created in Step 5
robocopy "apps\web\dist"   \\SERVER\E$\inetpub\vdata\web   /MIR /XF web.config
robocopy "apps\admin\dist" \\SERVER\E$\inetpub\vdata\admin /MIR /XF web.config
```

Then hard-refresh the browser once (**Ctrl+F5**).

`/XF web.config` matters — without it, `/MIR` deletes your `web.config` because it isn't part of the
build output, and the site immediately starts 404ing on every deep link.

**Keep a copy of `web.config` somewhere safe.** It is the one file that isn't in the build and isn't
in the repository.

Rebuild whenever the API address changes, too — it is baked into the bundle at build time.

---

## Troubleshooting

| What you see | What it means |
| --- | --- |
| **500.19** — internal server error, config | URL Rewrite Module isn't installed (IIS doesn't understand `<rewrite>`), or the `web.config` has a typo. Install it and restart IIS. |
| **404 on the home page** | `index.html` is one level too deep. You copied the `dist` folder instead of its contents. |
| **Home page works, inner pages 404 on refresh** | The SPA fallback rule isn't active — `web.config` missing, saved as `web.config.txt`, or URL Rewrite not installed. |
| **403 — Forbidden** | IIS can't read the folder. Re-run the `icacls` commands in [Step 6d](#6d-give-iis-permission-to-read-the-files). |
| **Blank white page** | Open F12 → Console. Usually a wrong `VITE_API_BASE_URL` (missing trailing slash) — fix the `.env.production` file, rebuild, redeploy. |
| **"blocked by CORS policy"** in the console | The site's origin isn't on the API's allow-list. [Step 8](#step-8--allow-the-new-origins-on-the-api), then restart the API app pool. |
| **Login works, but refreshing signs you out** | The site is being served over HTTP. The refresh cookie is `Secure` and the browser discarded it. Use HTTPS. |
| **All API calls fail, console mentions "mixed content"** | The page is HTTPS but `VITE_API_BASE_URL` is `http://`. Fix it, rebuild, redeploy. |
| **Old version still showing after an update** | `index.html` was cached. Confirm the `<location path="index.html">` block is in `web.config`, then Ctrl+F5. |
| **Redirect loop** | The `HTTPS redirect` rule is active behind a proxy that already terminates TLS. Delete that rule. |
| **`pnpm install` errors** | Run it from `frontend\`, not from `apps\web` or `apps\admin`. |
| **Site works locally by IP but not by name** | DNS isn't pointing at the server yet, or the binding's host name doesn't match what you typed. |

---

## Quick reference

| Item | Value |
| --- | --- |
| API base URL (build-time) | `https://vdataapi.nen-global.org/api/v1/` |
| API Swagger | <https://vdataapi.nen-global.org/swagger/index.html> |
| Web files | `E:\inetpub\vdata\web` ← `frontend\apps\web\dist` |
| Admin files | `E:\inetpub\vdata\admin` ← `frontend\apps\admin\dist` |
| App pool | `vdataStatic` — **No Managed Code** |
| Required IIS module | URL Rewrite 2.1 |
| Must be HTTPS | Yes — both sites |
| Must be on API CORS list | Yes — both origins |
