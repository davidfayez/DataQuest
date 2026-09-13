# Deploy package — 2026-08-15 15:30

Only the files that differ from bundle `iis-full-20260812-0315` (the last one you deployed). Each
one is byte-identical to the same file in the full bundle `iis-full-20260815-1530`; verified by
SHA-256 while packaging.

**31 files: 9 API, 5 admin, 13 web, plus 2 SQL scripts you no longer have to run, the
`api-web.config.reference` and `fix-put-delete.ps1`.**

---

## The database now migrates itself

**This release applies its own migrations.** On boot the API brings the database up to the schema
the binaries expect and adds any new permissions to the catalogue, then starts serving. Deploying
is: copy the files, restart the app pool.

What that removes:

- No migration script to run by hand, and no forgetting one.
- No window where new binaries serve traffic against the old schema — the API does not accept a
  request until the database matches it.
- No `Invalid column name` puzzles from replaying migrations the database already has: EF applies
  only what is missing, read from `__EFMigrationsHistory`.

Only one instance ever does the work. It takes a SQL application lock first, so an overlapped
app-pool recycle or a web garden cannot migrate twice — the second instance waits, finds nothing
pending, and starts.

Two things it deliberately does **not** do:

1. **It does not grant anything.** New permissions appear in the catalogue so they can be assigned;
   giving them to an account is still your decision, made in **Admin users**. A grant takes effect
   at that account's **next sign-in**.
2. **It does not seed.** Demo data and the SuperAdmin password reset stay Development-only.

To turn it off — where a DBA applies migrations out of band — set `Database__MigrateOnStartup` to
`false` in the API's `web.config`, or `"Database": { "MigrateOnStartup": false }` in
`appsettings.json`. The API will then start against whatever schema it finds, which is only safe if
that schema is already current.

`database\01-schema.sql` and `database\02-new-permissions.sql` are still in the package for a
belt-and-braces deploy, or if you prefer to run the schema change yourself in a maintenance window
before copying the binaries. **Running them is optional now.** If you do run them, run
`01-schema.sql` — not the `01-schema-idempotent.sql` from the earlier package, which fails on a
production database (see the note at the bottom).

---

## Step 1 — API (9 files)

```powershell
Stop-WebAppPool  -Name "<your api pool>"
copy /Y api\*.*  E:\inetpub\DataVerification\api\
Start-WebAppPool -Name "<your api pool>"
```

Four DLLs, their four `.pdb` files, and `appsettings.json` (which gains the new `Database` section).
The `.pdb`s are optional — they only make stack traces in the logs readable.

**Do not copy a `web.config` into `api\`.** The live one holds your secrets in
`<environmentVariables>` — `ConnectionStrings__DefaultConnection`, `Jwt__SigningKey`,
`Security__EncryptionKey`. That is why this bundle ships the file as `api-web.config.reference`
instead: it is there to compare against, not to copy over.

If `Security__EncryptionKey` is ever lost, the stored SendGrid key and the saved order passwords
become unreadable. Keep a copy off the server.

**After the pool restarts**, the API log will say what it did:

```
Applying 2 pending migration(s): 20260812125615_AddToolResources, 20260812164807_AddSubTransactionTypeCountries.
Database schema updated.
Permission catalogue synced: 7 added, 0 updated, 0 removed.
```

or, if you already ran the SQL scripts:

```
Database schema is up to date.
Permission catalogue is up to date.
```

## Step 2 — Grant the new permissions

In **Admin users**, open your account and tick what you want: **Tools** (the new Tools & guides
page), **Settings**, and **Orders → Reveal password**. Sign out and back in — permissions are baked
into the token at sign-in.

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

Included unchanged. If **Set up your order** already works on the live site, you have run it and can
ignore it. If a `PUT` still answers `405` with `Allow: GET, HEAD, OPTIONS, TRACE`, IIS's WebDAV
module is claiming the verb before ASP.NET Core sees it; run it from an elevated PowerShell on the
server:

```powershell
.\fix-put-delete.ps1 -AppPool "<your api pool>" -SiteName "<your api site>"
```

---

## What is in this release

**The database migrates itself on boot** — as above.

**Admin panel — creating and editing moved out of popups onto full pages.** Countries, Currencies,
Transaction types, Sub-transaction types, Verification authorities and Admin users each open their
own page: the fields grouped into titled sections, a status and live-summary column beside them,
and a Save bar pinned to the foot so it stays reachable however long the form runs. The permission
matrix on an admin user now gets the full width of the page instead of a scrollbar inside a dialog.

**Dropdowns are no longer clipped.** The panel that frames each section was cutting open dropdown
lists off at its edge — on the authority page you could see the search box and nothing else.

**Tools & guides** (new page): videos and images managed from the admin panel and shown to
applicants under **Tools** in the site header.

**A sub-transaction type carries its own country list**, restricted to the countries its parent
transaction type covers — the API rejects anything outside that set.

**Verification authorities** map sub-types through a searchable multi-select listed as
*transaction type – sub-type*, instead of a plain list of sub-type names.

**Applicant site:** documents can be previewed before submitting; the express-delivery note appears
only when Express is ticked, and reads more clearly; the header shows **Tools**.

---

## Verified before packaging

- **The automatic migration was tested against a copy of a database in your exact state** — the live
  database restored to a scratch copy, then rolled back: the two new tables dropped, their
  `__EFMigrationsHistory` rows deleted, and the seven new permission rows removed. Starting these
  binaries against it in Production mode applied both migrations and added all seven permissions,
  then served traffic. Restarting logged "up to date" and changed nothing.
- **The concurrent case was tested too** — two instances started against that behind database at the
  same moment. One migrated; the other waited on the lock, found nothing pending, and started
  clean. Both healthy, no duplicate work, no error.
- API business suite: **97 checks, 0 failures** — registration, wallet, the full application
  lifecycle through payment, review, refund, lookups CRUD, RBAC, settings and the authorization
  boundaries.
- Admin browser suite: **24 of 27** — the three failures all sign in as a demo `reviewer@` account
  that the seeder no longer creates; no product code is involved.
- `typecheck`, `lint` and the production build are clean for every package.

---

### Note on the earlier package

If you still have `delta-20260814-1736`, its first version shipped an `01-schema-idempotent.sql`
that fails on a production database with `Invalid column name 'CountryId'` / `'Title'` / `'Body'`.
That script covered every migration since the project began, including July ones that copy data out
of columns the same migrations then drop. SQL Server compiles a whole batch before running it, so
those column names are checked even though the `IF NOT EXISTS` guard would skip the code. It is
replaced by `01-schema.sql` here — and with this release you do not need to run either one.
