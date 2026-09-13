# API verification suites

End-to-end checks that drive the real HTTP surface. They complement the xUnit domain tests
(`backend/tests/DataVerification.UnitTests`), which cover the status state machine and wallet
ledger in isolation, by asserting the things only a live API can show: status codes, the stable
`code` on every ProblemDetails, cross-order isolation, and payload-level guarantees.

## Running

Start the API with the rate limits relaxed — registration and sign-in are throttled per IP by
default, which an automated run will trip:

```powershell
$env:RateLimiting__Registration__PermitLimit   = "5000"
$env:RateLimiting__Authentication__PermitLimit = "5000"
dotnet run --project backend/src/DataVerification.API
```

Then, from this directory:

```powershell
./run-all.ps1
```

Each suite is also runnable on its own, and all of them are **idempotent** — they generate unique
emails, order numbers and country codes per run, so repeated executions against the same database
do not collide.

## What each suite covers

| Suite | Checks |
| ----- | ------ |
| `verify-auth` | Registration, credential format (12-char unambiguous order number, 8-char password), generic 401s that do not reveal whether an account exists, order setup, cross-realm token rejection |
| `verify-lookups` | The full cascade scoped to the order's country, admin CRUD, bilingual search, referential safety (deactivate-instead-of-delete), RBAC |
| `verify-applications` | Cascade validation, server-side pricing, express rejection, magic-byte file validation, size limits, mandatory-file gating, order isolation |
| `verify-wallet` | Multi-application atomic payment, ledger reconciliation, post-payment lockdown, refunds, insufficient funds with required/available amounts |
| `verify-review` | Status workflow, **internal comments never reaching an applicant payload**, resubmission, result delivery, audit coverage |
| `verify-admin-platform` | Dashboard aggregates, order search, roles and permissions, admin users, audit log filters, per-permission RBAC |

## A note on the encoding

These scripts contain Arabic assertions. PowerShell 5.1 reads `.ps1` files in the system ANSI
codepage unless the file carries a UTF-8 **BOM**, which mangles non-Latin literals — so these
files are saved with one deliberately. (JSON files in this repository must *not* have a BOM; see
the root README.)
