# Delta package — 2026-08-12 03:15

The files that differ from bundle `iis-full-20260811-1740`. Every one is byte-identical to the
corresponding file in the full bundle `iis-full-20260812-0315`; verified by SHA-256 before
packaging.

Safe whether or not you deployed the 2336 bundle — this is the superset of both changes.

**`web\` is not included: the applicant SPA has not changed since 1740.**

---

## Step 1 — Fix PUT and DELETE (nothing else works until this is done)

Your live API still answers:

```
PUT /api/v1/orders/setup  ->  405   Allow: GET, HEAD, OPTIONS, TRACE
```

That is IIS's WebDAV module claiming `PUT` and `DELETE` before ASP.NET Core sees them. It is why
**Set up your order** fails, and it would also block saving the SendGrid key.

Copy `fix-put-delete.ps1` to the server, run in an **elevated** PowerShell:

```powershell
.\fix-put-delete.ps1 -AppPool "<your api pool>" -SiteName "<your api site>"
```

If the final line still says 405, run again adding `-DisableWebDavFeature`.

The script also generates `Jwt__SigningKey` and `Security__EncryptionKey` if they are missing, and
prints them once — copy them into your password manager.

## Step 2 — API (6 files)

```powershell
Stop-WebAppPool -Name "<your api pool>"
copy /Y api\*.* E:\inetpub\DataVerification\api\
Start-WebAppPool -Name "<your api pool>"
```

`DataVerification.Domain.dll` and everything else are unchanged. The `.pdb` files are optional —
they only improve stack traces in the logs.

## Step 3 — Admin (2 files)

```powershell
copy /Y admin\index.html               E:\inetpub\DataVerification\admin\
copy /Y admin\assets\index-Utr5BtMx.js E:\inetpub\DataVerification\admin\assets\
del      E:\inetpub\DataVerification\admin\assets\index-DXx5_aOA.js
```

The `del` is optional tidying — `index.html` already points at the new bundle. Then **Ctrl+F5**
once in the browser.

---

## What these files fix

**Uploading a document works again.** The upload content scanner searched the whole file for very
short markers. Photo data and compressed PDF streams are effectively random, so those markers
appeared by chance. Measured against the real scanner before the fix:

| File | Rejected |
| --- | --- |
| Ordinary JPEG, 50 KB and above | 20/20 (100%) |
| Ordinary PDF, 200 KB and above | 20/20 (100%) |
| A Word/scanner PDF with `/OpenAction [dest /FitH]` | rejected |

Three causes: `<%` is two bytes and occurs in essentially every photograph; `MZ` likewise matched
about half of them; and PDF markers were matched case-insensitively inside compressed streams,
with `/OpenAction` — which Word and most scanners write routinely — on the block list.

Now: markup is checked only in the first 4 KB (the window a browser sniffs); a PDF is searched
through its structure with `stream … endstream` payloads excluded; PDF names match case-sensitively
and must end at a name boundary, as the specification defines them. Rejections are logged with the
marker and offset, so the next one is diagnosable rather than opaque.

Verified 34/34 both ways: JPEG, PNG and PDF from 50 KB to 4 MB all accepted (0/25 rejected at every
size), while all seven PDF active-content markers, all eight markup polyglots, and files that are
themselves MZ or ELF binaries are still rejected.

**Editing a country works again** (from the 2336 bundle, included here). The validator demanded a
phone code that the seeded catalogue supplies for Egypt only — 188 of 189 countries had none, and
the form pre-filled a bare `+`, which fails the format rule. The prefix is now optional; the format
is still enforced when one is supplied.

A trade-off worth knowing: a PDF hiding JavaScript inside a compressed object stream will now pass
the scanner. That is the price of not rejecting every real document. The defences that actually
matter are unchanged — files are stored outside the web root, served as attachments, and never
executed.

---

## Confirm

```powershell
try { Invoke-WebRequest -Uri 'https://vdataapi.nen-global.org/api/v1/orders/setup' -Method Put -UseBasicParsing }
catch { "PUT now returns HTTP $([int]$_.Exception.Response.StatusCode)" }
```

**401 = fixed.** The verb reaches the application, which correctly rejects a call carrying no token.
405 = still blocked. No credentials are sent, so it cannot change data.

Then in the browser:

1. Register an order and complete **Set up your order**.
2. Upload a real scanned certificate on the documents tab — a normal multi-hundred-KB file.
3. **Lookups → Countries**: edit a country and save without touching the phone code.

`api-web.config.reference` is a complete, correct `web.config` for comparison, or to start from if
the site has none. Do not copy it over a working file without filling in your connection string.
