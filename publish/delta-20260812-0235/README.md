# Delta package — 2026-08-12

Only the files that differ from bundle `iis-full-20260811-1740`. Every file here is byte-identical
to the one in the full bundle `iis-full-20260811-2336`; verified by SHA-256 before packaging.

**`web\` is not included — the applicant SPA has not changed at all.**

---

## Step 1 — Fix PUT and DELETE (do this first; nothing else works without it)

Your live API currently answers:

```
PUT    /api/v1/orders/setup  ->  405   Allow: GET, HEAD, OPTIONS, TRACE
```

That is IIS's WebDAV module claiming `PUT` and `DELETE` before ASP.NET Core sees them. `GET` and
`POST` are unaffected, which is why registering works and **Set up your order** does not — and why
saving the SendGrid key would fail too.

Copy `fix-put-delete.ps1` to the server and run it in an **elevated** PowerShell:

```powershell
.\fix-put-delete.ps1 -AppPool "<your api pool>" -SiteName "<your api site>"
```

If the last line still reports 405, run again adding `-DisableWebDavFeature`.

The script also **generates `Jwt__SigningKey` and `Security__EncryptionKey` if they are missing**,
and prints them. See "About the generated keys" below.

## Step 2 — Copy the changed API files

```powershell
Stop-WebAppPool -Name "<your api pool>"
copy /Y api\*.* E:\inetpub\DataVerification\api\
Start-WebAppPool -Name "<your api pool>"
```

Six files: `DataVerification.API`, `.Application` and `.Infrastructure` (`.dll` + `.pdb`).
`DataVerification.Domain.dll` and everything else are unchanged, so leave them.

The `.pdb` files are optional — they only improve stack traces in logs.

## Step 3 — Copy the changed admin files

```powershell
copy /Y admin\index.html                     E:\inetpub\DataVerification\admin\
copy /Y admin\assets\index-Utr5BtMx.js       E:\inetpub\DataVerification\admin\assets\
del      E:\inetpub\DataVerification\admin\assets\index-DXx5_aOA.js
```

The last line removes the superseded bundle. It is optional — `index.html` already points at the
new file — but it keeps the folder clean.

Then **Ctrl+F5** in the browser once.

---

## What these files change

- **Editing a country works again.** The validator demanded a phone code, but the seeded catalogue
  supplies one for Egypt only — 188 of 189 countries had none, and the form pre-filled a bare `+`,
  which fails the format rule. Saving any seeded country returned 400, shown as "Something went
  wrong". The prefix is now optional; the format is still enforced when one is supplied.
- Development-only email setting (no effect in production).

---

## About the generated keys

`fix-put-delete.ps1` only generates a key when one is genuinely absent. It treats a value as
already configured if it is set in `web.config`, or in `appsettings.Production.json`, or in
`appsettings.json` — and it ignores the committed `REPLACE_…` placeholders. Verified against all
three cases: missing → generated, already in web.config → untouched, provided by
appsettings.Production.json → untouched.

**`Jwt__SigningKey`** — signs access tokens. Without an override the API uses the placeholder
committed to the repository, so anyone who can read the repo can forge an admin token. Replacing it
signs everyone out once; that is expected and desirable.

**`Security__EncryptionKey`** — encrypts the SendGrid API key and order passwords. **Back it up off
the server.** If it is lost or changed, the stored SendGrid key must be re-entered and any order
passwords saved under it can never be revealed.

The script prints both values in a boxed block. Copy them into your password manager before closing
the window.

`api-web.config.reference` is a complete, correct `web.config` — for comparison, or to start from if
the site has none. **Do not copy it over a working file without filling in your connection string
first.**

---

## Confirm

```powershell
try { Invoke-WebRequest -Uri 'https://vdataapi.nen-global.org/api/v1/orders/setup' -Method Put -UseBasicParsing }
catch { "PUT now returns HTTP $([int]$_.Exception.Response.StatusCode)" }
```

**401 = fixed.** The verb reaches the application, which correctly rejects a request carrying no
token. 405 = still blocked. The call sends no credentials, so it cannot change data.

Then in the browser: register an order and complete **Set up your order**; open
**Lookups → Countries**, edit a country and save without touching the phone code.
