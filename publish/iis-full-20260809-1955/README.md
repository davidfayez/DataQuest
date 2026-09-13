# Release bundle — 2026-08-09

Contains the API, both SPAs, and the database scripts for this release.

## What changed

- **Reveal an order's password** — `GET /api/v1/admin/orders/{orderId}/password`, gated on the new
  `Orders.ViewPassword` permission. Registration now also stores the generated password encrypted,
  alongside the existing hash. **Orders created before this release have nothing to reveal.**
- **SendGrid API key managed from the admin panel** — new **Settings** page
  (`/settings` in admin), gated on `Settings.View` / `Settings.Update`. The key is stored
  encrypted in `SiteSettings` and read per send, so rotating it needs no redeploy.
- **Schema**: one new nullable column, `Orders.PasswordSecret`.

---

## 🔴 Read before copying anything

**1. Do not overwrite the API's `web.config`.** All your production secrets — connection string,
`Jwt__SigningKey`, CORS origins — live in `<environmentVariables>` in the copy on the server. The
`web.config` in `api\` here is the plain one `dotnet publish` generates and has **none of them**.
Copying it over takes the site down.

Always exclude it:

```powershell
robocopy "<bundle>\api" E:\inetpub\DataVerification\api /MIR /XF web.config appsettings.Production.json
```

**2. New required setting.** Both new features need an encryption key. Without it the platform runs
normally, but passwords cannot be revealed and the settings page disables itself. Add to the API's
`web.config`, inside the existing `<environmentVariables>` block:

```xml
<environmentVariable name="Security__EncryptionKey" value="PASTE_32_BYTE_BASE64_HERE" />
```

Generate one (any machine):

```powershell
$b = New-Object byte[] 32
[System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($b)
[Convert]::ToBase64String($b)
```

**Back this value up.** Change it later and everything stored under the old key becomes
unreadable — you would have to re-enter the SendGrid key and old passwords stay hidden.

**3. Email transport selection changed.** SendGrid used to be chosen at startup only when a key was
present in configuration; with an empty `SendGrid:ApiKey` the API was silently on the *log-only*
sender and delivering nothing. Now `SendGrid:Enabled` alone selects it and the key is resolved per
send: **database first, then `SendGrid__ApiKey`, then nothing**. If you already set
`SendGrid__ApiKey` on the server it keeps working unchanged, and the Settings page will report the
source as *Configuration* until you save a key there.

---

## Order of operations

Schema first, then the API, then the SPAs. The new API needs the new column; the old API tolerates
it, so there is no window where the running site is broken.

### 1. Database

Against **VData_Prod**, in this order:

| File | What it does |
| --- | --- |
| `database\01-add-order-password-secret.sql` | Adds the `Orders.PasswordSecret` column and records the migration. Guarded by the `__EFMigrationsHistory` check, so re-running it does nothing. |
| `database\02-new-permissions.sql` | Creates the three new permission rows and (optionally) grants them. **Edit `@GrantToEmail` at the top first.** |

```powershell
sqlcmd -S <server> -d VData_Prod -U <user> -P <password> -C -b -i "database\01-add-order-password-secret.sql"
sqlcmd -S <server> -d VData_Prod -U <user> -P <password> -C -b -i "database\02-new-permissions.sql"
```

`-b` makes sqlcmd return a non-zero exit code on error instead of carrying on quietly.

Both scripts were run against a real database before shipping, twice each, to confirm they are
idempotent. `02` deliberately fails loudly if `@GrantToEmail` matches no admin user.

Take a backup first. `01` only adds a nullable column, but a backup costs a minute.

> This release assumes the database is at `20260802173703_AddAdminAttachedDocuments`. If it is
> further behind, run the API once with `Database__MigrateOnStartup=true` instead, or generate a
> wider script with `dotnet ef migrations script <from> <to> --idempotent`.

### 2. API

```powershell
# Stop the app pool so the DLLs are not locked
Stop-WebAppPool -Name "<your api pool>"

robocopy "<bundle>\api" E:\inetpub\DataVerification\api /MIR /XF web.config appsettings.Production.json

# Add Security__EncryptionKey to web.config now (see above), then:
Start-WebAppPool -Name "<your api pool>"
```

Verify: <https://vdataapi.nen-global.org/swagger/index.html> loads and lists
`/api/v1/admin/settings/email`.

### 3. Web and Admin

Both are static. `/XF web.config` keeps the file already on the server — without it `/MIR` deletes
it and every deep link starts returning 404.

```powershell
robocopy "<bundle>\web"   E:\inetpub\DataVerification\web   /MIR /XF web.config
robocopy "<bundle>\admin" E:\inetpub\DataVerification\admin /MIR /XF web.config
```

If a site has no `web.config` yet, copy `spa-web.config` in as `web.config` (same file for both).

Then **Ctrl+F5** once in the browser — `index.html` is served no-cache, but an already-open tab may
still be holding the old bundle.

---

## After deploying

1. Sign out of the admin panel and back in — new permissions are baked into the token at login, so
   an existing session will not have them.
2. Open **Settings** in the sidebar. It should say *Using the key from server configuration* if
   `SendGrid__ApiKey` is set, or *No API key is configured* if not.
3. Paste the SendGrid key and save. The status line switches to *Using the key set on this page*.
4. Register a test order and confirm the credentials email arrives.
5. Open any order and check the reveal endpoint. Orders created **before** this deploy return
   `isAvailable: false, reason: "not_stored"` — that is expected and permanent for them.

## Rollback

Redeploy the previous bundle over `api\` (again excluding `web.config`). The new column is nullable
and the old build ignores it, so no schema rollback is needed. The stored SendGrid key is also
ignored by the old build — it falls back to `SendGrid__ApiKey`, so make sure that is still set
before rolling back.

## Contents

```
api\                          dotnet publish output, Release, framework-dependent (needs the
                              ASP.NET Core 10 Hosting Bundle on the server)
web\                          applicant SPA — copy the CONTENTS to the site root
admin\                        back-office SPA — copy the CONTENTS to the site root
spa-web.config                web.config for both SPA sites, only if one is not already there
database\01-add-order-password-secret.sql
database\02-new-permissions.sql
vdata-api.zip / vdata-web.zip / vdata-admin.zip    the same three folders, zipped
```

Both SPA bundles have `https://vdataapi.nen-global.org/api/v1/` compiled in. If the API moves, they
must be rebuilt — the address cannot be changed after the build.
