# Deploy package — 2026-08-14 17:36

Only the files that differ from bundle `iis-full-20260812-0315` (the last one you deployed). Each
one is byte-identical to the same file in the full bundle `iis-full-20260814-1736`; verified by
SHA-256 while packaging.

**29 files: 8 API, 5 admin, 13 web, 2 SQL, plus `fix-put-delete.ps1` and the web.config reference.**

Do the database step first. The API will start either way, but the two new features fail until the
tables exist.

---

## Step 1 — Database (once)

Run against the production database, in this order, with SSMS or `sqlcmd`:

```powershell
sqlcmd -S <server> -d VData_Prod -i database\01-schema.sql
sqlcmd -S <server> -d VData_Prod -i database\02-new-permissions.sql
```

**`01-schema.sql`** adds two tables and their indexes — nothing else. No existing table, column or
row is altered or dropped:

| Migration | What it adds |
| --- | --- |
| `20260812125615_AddToolResources` | `ToolResources`, `ToolResourceTranslations` — the Tools & guides page |
| `20260812164807_AddSubTransactionTypeCountries` | `SubTransactionTypeCountries` — the country list on a sub-transaction type |

Both blocks are guarded by a `__EFMigrationsHistory` check, so running it twice is a no-op.

> **If you have an earlier copy of this bundle**, its `01-schema-idempotent.sql` fails on a
> production database with `Invalid column name 'CountryId'` / `'Title'` / `'Body'`. That script
> covered *every* migration since the beginning, including July ones that copy data out of columns
> which the same migrations then drop. SQL Server compiles a whole batch before it runs, so those
> column names are checked even though the `IF NOT EXISTS` guard would skip the code. Use
> `01-schema.sql` here instead — it starts from `AddOrderPasswordSecret`, the state your database is
> already in.

**`02-new-permissions.sql`** is the same script that shipped in the 2026-08-12 bundle. If you
already ran it, run it again anyway — it checks before every write and will simply report the rows
are present. Open it first and set `@GrantToEmail` to the administrator who should receive the new
permissions (Tools, Settings, Reveal password), or leave it `NULL` and assign them per user from
**Admin users** in the panel.

The grant only takes effect at the **next sign-in** — permissions are baked into the token at login.

## Step 2 — API (8 files)

```powershell
Stop-WebAppPool  -Name "<your api pool>"
copy /Y api\*.*  E:\inetpub\DataVerification\api\
Start-WebAppPool -Name "<your api pool>"
```

Four DLLs and their four `.pdb` files. The `.pdb`s are optional — they only make stack traces in
the logs readable. Everything else in `api\` is unchanged.

**Do not copy a `web.config` into `api\`.** The live one holds your secrets in
`<environmentVariables>` — `ConnectionStrings__DefaultConnection`, `Jwt__SigningKey`,
`Security__EncryptionKey`. That is why this bundle ships the file as `api-web.config.reference`
instead: it is there to compare against, not to copy over.

If `Security__EncryptionKey` is ever lost, the stored SendGrid key and the saved order passwords
become unreadable. Keep a copy off the server.

## Step 3 — Admin panel (5 files)

```powershell
copy /Y admin\index.html   E:\inetpub\DataVerification\admin\
copy /Y admin\assets\*.*   E:\inetpub\DataVerification\admin\assets\
```

## Step 4 — Applicant site (13 files)

```powershell
copy /Y web\index.html     E:\inetpub\DataVerification\web\
copy /Y web\assets\*.*     E:\inetpub\DataVerification\web\assets\
```

Then **Ctrl+F5** once in each site. `index.html` already points at the new bundles, so the old
files below are dead weight rather than a problem — delete them when convenient:

```
admin\assets\  ar-PsUL3n8N.js  en-DD5XFixi.js  index-DQmGhqjk.css  index-Utr5BtMx.js
web\assets\    ar-ww7nkFnI.js  de-BhpRkrM0.js  en-DHT7PAF6.js  hi-BtLr_y3T.js
               index-9IU3XRMd.css  index-BlCDLJc1.js  ja-DNJAU-66.js  pl-BNFvhGN7.js
               ru-nS0ab7Mk.js  tr-D5e3ZG4W.js  uz-DIpyG1t9.js  zh-CghvpGDW.js
```

## `fix-put-delete.ps1` — only if PUT or DELETE returns 405

Included unchanged from the last bundle. If **Set up your order** already works on the live site,
you have run it and can ignore it. If a `PUT` still answers `405` with
`Allow: GET, HEAD, OPTIONS, TRACE`, IIS's WebDAV module is claiming the verb before ASP.NET Core
sees it; run the script from an elevated PowerShell on the server:

```powershell
.\fix-put-delete.ps1 -AppPool "<your api pool>" -SiteName "<your api site>"
```

Add `-DisableWebDavFeature` if the final line still says 405.

---

## What is in this release

**Admin panel — creating and editing moved out of popups onto full pages.** Countries, Currencies,
Transaction types, Sub-transaction types, Verification authorities and Admin users each open their
own page: the fields grouped into titled sections, a status and live-summary column beside them,
and a Save bar pinned to the foot so it stays reachable however long the form runs. The permission
matrix on an admin user now gets the full width of the page instead of a scrollbar inside a dialog.

**Dropdowns are no longer clipped.** The panel that frames each section was cutting open dropdown
lists off at its edge — on the authority page you could see the search box and nothing else.

**Tools & guides** (new page, needs the new tables): videos and images managed from the admin panel
and shown to applicants under **Tools** in the site header.

**A sub-transaction type carries its own country list**, restricted to the countries its parent
transaction type covers — the API rejects anything outside that set.

**Verification authorities** map sub-types through a searchable multi-select listed as
*transaction type – sub-type*, instead of a plain list of sub-type names.

**Applicant site:** documents can be previewed before submitting; the express-delivery note appears
only when Express is ticked, and reads more clearly; the header shows **Tools**.

---

## Verified before packaging

- **The database scripts were run against a copy of a database in your exact state** — the live
  database restored to a scratch copy, the two new migrations rolled back off it, then
  `01-schema.sql` and `02-new-permissions.sql` each run twice. Both succeeded, and the second run
  changed nothing.
- **The published binaries in this bundle were then pointed at that freshly migrated database**, in
  Production mode: Tools & guides created and read back, and a country attached to a
  sub-transaction type and read back. So the DLLs and the schema in this bundle match.
- API business suite: **97 checks, 0 failures** — registration, wallet, the full application
  lifecycle through payment, review, refund, lookups CRUD, RBAC, settings and the authorization
  boundaries.
- Admin browser suite: **24 of 27** — the three failures all sign in as a demo `reviewer@` account
  that the seeder no longer creates; no product code is involved.
- `typecheck`, `lint` and the production build are clean for every package.
