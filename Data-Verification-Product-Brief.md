# Data Verification — Product Brief & Claude Code / Cursor Execution Prompts

**Author role:** Business Analyst / Product Owner / Project Manager
**Product name:** Data Verification
**Default Client:** NEN (seeded as the only record in `Clients` table — every Order is created with `ClientId = NEN` by default)
**Deliverables:** Public Web App (React) + Admin Panel (React) + REST API (.NET 10 + SQL Server)

---

## 1. Product Overview

Data Verification is a platform where applicants create an **Order** using only their email, receive auto-generated credentials, and then submit one or more **Applications** to verify official documents (educational, professional, or security certificates) through verification authorities in a chosen country. Payment is handled through a **per-Order wallet**. Admins manage all lookups, review applications, exchange comments with applicants (plus internal admin-only comments), and deliver final verified files.

### Core business rules (must never be violated)

1. An **Order** has many **Applications**. One Order = one email + one 12-character OrderNumber + one 8-character generated password + one Verification Country + one Currency + one Wallet.
2. `Currency` options depend on the selected `Country` (a Country holds a list of Currencies — managed in Admin).
3. Cascading lookups (all admin-managed, all country-aware):
   - **Transaction Type (نوع المعاملة)** depends on **Verification Country**.
   - **Sub-Transaction Type (نوع المعاملة الفرعية)** depends on **Transaction Type**.
   - **Verification Authority (جهة التحقق)** depends on **Sub-Transaction Type + Verification Country**.
   - **Service Type (نوع الخدمة)** depends on **Verification Authority**. Service Type configuration controls `EnableExpress` availability, cost, express cost, execution time, and the list of required files.
4. **Application status lifecycle:** `Draft → PendingPayment → Pending (Paid) → InProgress → MissedInfo → InProgress → Success | Failed`.
   - **Edit / Delete:** allowed only while the application is **not paid** (Draft / PendingPayment).
   - **Refund:** allowed only while the application status is **Pending** (paid but not yet started). Refund credits the Order wallet.
   - **InProgress, MissedInfo, Success, Failed:** user **cannot** edit, delete, or refund.
5. **Payment:** wallet-based. User can select **multiple unpaid applications and pay them together** in one wallet transaction.
6. **Comments:** every admin comment on an application has a visibility flag — `ForUser` (visible to applicant) or `Internal` (admins only). Users see status **MissedInfo**, can reply, and the full thread is rendered as a well-designed chat history / activity log.
7. **Delivery:** when status = Success, admin attaches result files; user downloads them from the application page.
8. **Multi-language web app:** Arabic (ar), English (en), Russian (ru), Urdu (ur — Pakistan), German (de), Hindi (hi — India), Chinese (zh). **Arabic and Urdu are RTL** — full RTL layout support required. Admin panel: Arabic + English is sufficient (extendable).

---

## 2. Tech Stack & Architecture

### Frontend (Web App + Admin Panel)
- **React 19 + TypeScript + Vite** (two apps in one monorepo: `apps/web` and `apps/admin`, shared packages in `packages/`)
- State/server cache: **TanStack Query v5**; forms: **React Hook Form + Zod**; routing: **React Router v7**
- UI: **Tailwind CSS + shadcn/ui**, with a shared design-system package (`packages/ui`)
- i18n: **i18next + react-i18next** with ICU plurals, lazy-loaded locale bundles, `dir="rtl"` switching for ar/ur, locale-aware dates/numbers via `Intl`
- Architecture: **feature-sliced** (`features/`, `entities/`, `shared/`, `pages/`) — no business logic inside components; API access only through typed clients generated from the OpenAPI spec

### Backend
- **.NET 10 Web API**, **Clean Architecture** solution:
  - `DataVerification.Domain` — entities, enums, domain events, business rules (status state machine lives here)
  - `DataVerification.Application` — CQRS with **MediatR**, **FluentValidation**, DTOs, interfaces
  - `DataVerification.Infrastructure` — **EF Core 10 + SQL Server**, Identity, email (SMTP/SendGrid abstraction), file storage abstraction (local disk now, S3/Azure Blob ready), background jobs
  - `DataVerification.API` — minimal controllers, versioned endpoints (`/api/v1`), ProblemDetails errors, Serilog, Swagger/OpenAPI
- **Auth:** JWT. Two identity realms: `Applicant` (OrderNumber + generated password) and `AdminUser` (email + password, roles & permissions).
- **Permissions:** policy-based authorization; permissions stored in DB, grouped into Roles (e.g., `Countries.View`, `Applications.Review`, `Wallet.Refund`, `Lookups.Manage`, `Roles.Manage`).
- Audit logging on every application status change, comment, payment, and refund.

---

## 3. Database Schema (SQL Server)

> Naming: PascalCase tables, `Id` UNIQUEIDENTIFIER (or BIGINT identity — pick one and stay consistent), `CreatedAtUtc`, `UpdatedAtUtc`, soft delete `IsDeleted` where noted. All lookup tables carry localized name columns or a translations table (`{Entity}Translations` with `LanguageCode`).

| Table | Key columns | Notes |
|---|---|---|
| **Clients** | Id, Code, Name | Seed exactly one record: `NEN`. |
| **Countries** | Id, Code (ISO2), NameAr, NameEn, IsActive | Admin CRUD page. |
| **Currencies** | Id, Code (ISO 4217), NameAr, NameEn, Symbol | Admin CRUD page. |
| **CountryCurrencies** | CountryId, CurrencyId | Many-to-many: a country exposes a list of currencies. |
| **Orders** | Id, ClientId (FK → Clients, default NEN), Email, OrderNumber (CHAR 12, unique, indexed), PasswordHash, VerificationCountryId, CurrencyId, CreatedAtUtc | Password is the generated 8-char credential (stored hashed). |
| **Wallets** | Id, OrderId (1:1), Balance DECIMAL(18,2), CurrencyId | One wallet per Order. |
| **WalletTransactions** | Id, WalletId, Type (TopUp / Payment / Refund), Amount, ReferenceApplicationIds (JSON), PerformedBy, CreatedAtUtc | Full ledger — balance is always derivable from the ledger. |
| **IdentityTypes** | Id, NameAr, NameEn, IsActive | نوع الهوية: بطاقة الرقم القومي، شهادة الميلاد، رخصة القيادة، باسبور. Admin CRUD. |
| **TransactionTypes** | Id, NameAr, NameEn, CountryId, IsActive | نوع المعاملة — scoped by country. Examples: التحقق من الشهادات الدراسية / المهنية / الأمنية. |
| **SubTransactionTypes** | Id, TransactionTypeId, NameAr, NameEn, IsActive | نوع المعاملة الفرعية. أمنية → فيش جنائي، شهادة الخدمة العسكرية. دراسية → بكالوريوس، ماجستير، دكتوراه. مهنية → شهادة الخبرة. |
| **VerificationAuthorities** | Id, NameAr, NameEn, CountryId, IsActive | جهة التحقق. |
| **AuthoritySubTransactionTypes** | VerificationAuthorityId, SubTransactionTypeId | Junction: authority availability per sub-transaction type (+ country via authority). |
| **ServiceTypes** | Id, VerificationAuthorityId, NameAr, NameEn, Description, ExecutionTimeDays, Cost, EnableExpress BIT, ExpressCost, IsActive | نوع الخدمة — depends on authority; owns express config & pricing. |
| **ServiceTypeRequiredFiles** | Id, ServiceTypeId, NameAr, NameEn, IsMandatory | Number & definition of files depend on the Service Type. |
| **Applications** | Id, OrderId, ApplicationNumber, AddressedTo (الطلب موجه إلي), BirthDate, Status, TransactionTypeId, SubTransactionTypeId, VerificationAuthorityId, TotalCost, PaidAtUtc NULL, IsDeleted | Status enum: Draft, PendingPayment, Pending, InProgress, MissedInfo, Success, Failed, Refunded. |
| **ApplicationNames** | Id, ApplicationId, LanguageType (Arabic / English), FirstName, MiddleName NULL, LastName | First & Last required for both Arabic and English rows. |
| **ApplicationIdentities** | Id, ApplicationId, IdentityTypeId, CardNumber, IssueDate, ExpiryDate | Multiple identity sections per application. |
| **ApplicationServices** | Id, ApplicationId, ServiceTypeId, Quantity (Number), LanguageCode, IsExpress BIT, UnitCost, ExpressCost, LineTotal | Multiple rows per application. `IsExpress` only selectable if ServiceType.EnableExpress = 1. |
| **ApplicationFiles** | Id, ApplicationId, ApplicationServiceId NULL, RequiredFileId NULL, FileName, StoragePath, ContentType, SizeBytes, UploadedBy (User/Admin), Kind (UserUpload / AdminResult) | Validation: pdf, jpg, jpeg, png only; max 5 MB. `AdminResult` files are the downloadable deliverables after Success. |
| **ApplicationComments** | Id, ApplicationId, AuthorType (Admin / Applicant), AuthorId, Visibility (**ForUser** / **Internal**), Body, CreatedAtUtc | Internal comments never serialized to applicant endpoints — enforce at query level, not UI level. |
| **ApplicationStatusHistory** | Id, ApplicationId, FromStatus, ToStatus, ChangedByType, ChangedById, Note, CreatedAtUtc | Drives the activity/chat timeline. |
| **AdminUsers** | Id, Email, PasswordHash, FullName, IsActive | |
| **Roles / Permissions / RolePermissions / AdminUserRoles** | — | Standard RBAC; permissions are seeded constants checked by policy. |

**Seed data:** Client NEN; sample country (Egypt) with EGP + USD; the four identity types; the three transaction types with their sub-types; at least one authority + service type with required files, so the whole flow is demo-able on first run.

---

## 4. End-to-End User Flows

### 4.1 Order registration (passwordless start)
1. Visitor opens web app, picks UI language (7 languages, RTL for ar/ur), enters **Email** only.
2. API creates Order (`ClientId = NEN`), generates **OrderNumber (12 chars, unambiguous charset, unique)** and **Password (8 chars, cryptographically random)**, hashes the password, sends a **localized email** containing OrderNumber, Password, and a deep link to the login step.
3. Login step: user enters **OrderNumber + Password** → JWT issued for that Order.
4. First-time setup inside the Order: choose **Verification Country** → choose **Currency** (options filtered by the chosen country). Wallet is created in that currency.

### 4.2 Create New Application (multi-step wizard)
- **Step 1 — Addressed To:** text input, placeholder **"الطلب موجه إلي"**.
- **Step 2 — Personal Info:** Arabic name (First / Middle / Last) + English name (First / Middle / Last); First & Last mandatory in both. Birth date. **Identity sections (repeatable):** Identity Type (lookup) → Card Number, Issue Date, Expiry Date. User can add multiple identity sections.
- **Step 3 — Application Details (cascading):** Transaction Type (filtered by Order's Verification Country) → Sub-Transaction Type → Verification Authority (filtered by sub-type + country) → then **repeatable service rows**: Service Type (filtered by authority) + Number (quantity) + Language + Express toggle (visible/enabled only when the Service Type has `EnableExpress`).
- **Step 4 — Services Summary Table:** ServiceName, Description, ExecutionTime, Cost, EnableExpress, ExpressCost, LineTotal — plus the list of required files per service.
- **Step 5 — File Upload:** per required file definition; accept **pdf / jpg / jpeg / png**, max **5 MB**; client-side and server-side validation; show upload progress and per-file status.
- **Step 6 — Review & Save:** application saved as **Draft / PendingPayment**.

### 4.3 Payment, edit/delete, refund
- Applications list inside the Order shows status + payment status badges.
- User selects **one or many unpaid applications → Pay together** from the Order wallet (single WalletTransaction referencing all paid application IDs). Insufficient balance → top-up flow (out of scope for v1 gateway; admin can credit wallet).
- **Not paid (Draft/PendingPayment):** Edit and Delete enabled.
- **Pending (paid, not started):** Refund enabled → application → `Refunded`, amount credited back to wallet, ledger entry created.
- **InProgress / MissedInfo / Success / Failed:** Edit, Delete, and Refund all disabled (server-enforced, not just hidden buttons).

### 4.4 Review, comments & delivery
- Admin opens the application queue, moves status `Pending → InProgress`.
- Admin can post a comment with visibility **ForUser** (e.g., "الصورة مش واضحة، ارفعها تاني") — this flips status to **MissedInfo** — or **Internal** (admins only, never exposed to applicant APIs).
- Applicant sees MissedInfo, re-uploads the file, replies with a comment; status returns to InProgress on resubmit.
- The application page renders a **chat-style activity timeline** (comments + status changes + file events) — clean two-sided chat design, RTL-aware, with timestamps and author badges.
- On **Success**, admin uploads result files (`Kind = AdminResult`); applicant gets a Download section. On **Failed**, admin records the reason (ForUser comment).

---

## 5. Admin Panel Modules

1. **Dashboard** — counts by status, revenue, pending queue.
2. **Orders** — search by OrderNumber/email; view order, applications, wallet ledger; credit wallet; process refunds.
3. **Applications Queue** — filters (status, country, authority, date); detail page with tabs: Details / Files / Comments (User + Internal) / Timeline; status actions guarded by the state machine.
4. **Lookups** — Countries, Currencies, Country↔Currency mapping, Identity Types, Transaction Types (per country), Sub-Transaction Types, Verification Authorities (+ sub-type mapping), Service Types (cost, express, execution time, required files).
5. **Roles & Permissions** — create roles, assign granular permissions, assign roles to admin users.
6. **Admin Users** — CRUD + activation.
7. **Audit Log** — global searchable audit trail.

---

## 6. Epics, User Stories & Acceptance Criteria (abridged)

### Epic 1 — Order Onboarding
- **US1.1** As a visitor, I register with email only and receive OrderNumber (12) + Password (8) + login link by email, localized to my selected language.
  - AC: OrderNumber unique, 12 chars, no ambiguous characters (no 0/O, 1/l/I); password ≥1 upper, ≥1 lower, ≥1 digit; email delivered within 1 min; resend allowed with rate-limit.
- **US1.2** As an applicant, I log in with OrderNumber + Password and receive a scoped JWT.
  - AC: 5 failed attempts → temporary lockout; wrong credentials return a generic error.
- **US1.3** As an applicant, I select Verification Country then Currency (filtered by country); the choice locks the Order's wallet currency.

### Epic 2 — Application Wizard
- **US2.1** Personal info validation: Arabic & English First/Last required, Middle optional; birth date must be in the past.
- **US2.2** I can add/remove multiple identity sections; each requires Identity Type, Card Number, Issue Date, Expiry Date; Expiry > Issue.
- **US2.3** Cascading dropdowns never show stale options — changing a parent resets children; all lists filtered server-side by country/parent.
- **US2.4** Express toggle appears only when the Service Type allows it; line totals = (Cost + ExpressCost if express) × Number.
- **US2.5** File upload rejects wrong type/size with a localized message; mandatory files block submission.

### Epic 3 — Payment & Wallet
- **US3.1** I select multiple unpaid applications and pay them in one action; wallet debited atomically; each application → Pending with PaidAtUtc set.
  - AC: transaction is all-or-nothing; concurrent payment attempts on the same application are safe (row locking / optimistic concurrency).
- **US3.2** I can refund a Pending application; wallet credited; status → Refunded; ledger entry recorded.
- **US3.3** Edit/Delete blocked server-side for any paid application; Refund blocked for InProgress/MissedInfo/Success/Failed.

### Epic 4 — Review & Communication
- **US4.1** As an admin, I post a ForUser or Internal comment; ForUser comment sets status MissedInfo and (later) triggers an email notification.
- **US4.2** As an applicant, I never receive Internal comments in any API response (verified by integration test).
- **US4.3** The timeline merges comments, status changes, and file events in chronological order, chat-styled, RTL-aware.
- **US4.4** On Success, admin uploads result files; applicant sees a Download section with all AdminResult files.

### Epic 5 — Admin Platform
- **US5.1** RBAC: an admin without `Lookups.Manage` cannot open or call lookup CRUD endpoints (403).
- **US5.2** Full CRUD for all lookups with country scoping and Arabic/English names.
- **US5.3** Every status change, payment, refund, and comment is written to the audit log with actor + timestamp.

### Epic 6 — Internationalization
- **US6.1** The web app ships 7 locales: ar, en, ru, ur, de, hi, zh; language switcher persists choice; ar & ur flip the layout to RTL including the wizard, tables, and chat timeline.
- **US6.2** Dates, numbers, and currency render via `Intl` per active locale.

---

## 7. Execution Prompts for Claude Code / Cursor

> Run these **in order**. Each prompt is self-contained — paste it as-is. After each one, run the build/tests before moving on.

---

### 🔧 Prompt 0 — Monorepo & Solution Scaffold

```
You are a senior full-stack architect. Create a project called "Data Verification" with this structure:

/data-verification
  /backend            → .NET 10 solution "DataVerification" using Clean Architecture:
                        Domain, Application (MediatR CQRS + FluentValidation), Infrastructure (EF Core 10 + SQL Server), API (controllers, Swagger, Serilog, JWT auth, ProblemDetails, API versioning /api/v1)
  /frontend           → pnpm workspace monorepo:
                        apps/web (React 19 + TypeScript + Vite), apps/admin (React 19 + TypeScript + Vite),
                        packages/ui (shared shadcn/ui + Tailwind design system), packages/api-client (typed client), packages/i18n (shared i18next setup)

Requirements:
- Feature-sliced structure in both React apps: pages/, features/, entities/, shared/.
- ESLint + Prettier + strict TypeScript everywhere; EditorConfig; .gitignore.
- Backend: central package management, nullable enabled, analyzers on, docker-compose with SQL Server 2022 for local dev, appsettings with connection string + JWT + SMTP sections.
- Add a root README explaining how to run everything (docker compose up db, dotnet run, pnpm dev).
Do NOT implement business features yet. Verify: backend builds, both frontends start, health endpoint /api/v1/health returns 200.
```

---

### 🗄️ Prompt 1 — Domain Model, EF Core & Seed Data

```
In the DataVerification backend, implement the full domain and persistence layer.

Entities (Domain project, with proper relationships): Client, Country, Currency, CountryCurrency, Order, Wallet, WalletTransaction, IdentityType, TransactionType (has CountryId), SubTransactionType (has TransactionTypeId), VerificationAuthority (has CountryId), AuthoritySubTransactionType (junction), ServiceType (has VerificationAuthorityId, Cost, EnableExpress, ExpressCost, ExecutionTimeDays, Description), ServiceTypeRequiredFile, Application, ApplicationName, ApplicationIdentity, ApplicationService, ApplicationFile, ApplicationComment, ApplicationStatusHistory, AdminUser, Role, Permission, RolePermission, AdminUserRole.

Rules to encode in the Domain layer (not in controllers):
- ApplicationStatus enum: Draft, PendingPayment, Pending, InProgress, MissedInfo, Success, Failed, Refunded.
- Application.CanEdit()/CanDelete() → only Draft or PendingPayment (not paid).
- Application.CanRefund() → only Pending.
- Status transitions via a state machine method Application.TransitionTo(newStatus, actor) that throws DomainException on illegal transitions and appends ApplicationStatusHistory.
- Wallet.Debit/Credit create WalletTransaction ledger entries; balance can never go negative.
- ApplicationComment.Visibility enum: ForUser, Internal.
- ApplicationFile.Kind enum: UserUpload, AdminResult. Allowed content types: pdf, jpg, jpeg, png. Max size 5 MB (validated in Application layer too).

EF Core: configurations per entity (Fluent API), UNIQUE index on Order.OrderNumber, decimal(18,2) for money, migrations, and a DataSeeder that seeds:
- Client "NEN" (the only client; Orders default to it),
- Egypt with currencies EGP + USD,
- Identity types: بطاقة الرقم القومي، شهادة الميلاد، رخصة القيادة، باسبور,
- Transaction types for Egypt: التحقق من الشهادات الدراسية / المهنية / الأمنية with sub-types (أمنية: فيش جنائي، شهادة الخدمة العسكرية — دراسية: بكالوريوس، ماجستير، دكتوراه — مهنية: شهادة الخبرة),
- One verification authority + two service types (one with EnableExpress=true) each with 2 required files,
- Roles: SuperAdmin (all permissions), Reviewer; one seeded SuperAdmin user.

Add unit tests for the status state machine and wallet ledger. Verify: migrations apply cleanly to the docker SQL Server and seeding is idempotent.
```

---

### 🔐 Prompt 2 — Auth: Applicant (Order) + Admin RBAC

```
Implement authentication and authorization.

Applicant flow:
- POST /api/v1/orders/register { email, languageCode } → creates Order (ClientId = NEN), generates OrderNumber (12 chars, charset excludes 0,O,1,l,I; unique with retry) and password (8 chars, cryptographically random, ≥1 upper ≥1 lower ≥1 digit), stores password hashed (ASP.NET Identity hasher), sends localized email (templated per the 7 supported languages) containing OrderNumber, password, and a deep link {WEB_URL}/{lang}/login?order={OrderNumber}.
- POST /api/v1/orders/login { orderNumber, password } → JWT with claim orderId, role "Applicant". Rate-limit and lockout after 5 failures.
- PUT /api/v1/orders/setup { verificationCountryId, currencyId } → validates currency belongs to country, creates Wallet in that currency. Setup is immutable once an application exists.

Admin flow:
- POST /api/v1/admin/auth/login → JWT with role "Admin" + permission claims loaded from RBAC tables.
- Policy-based authorization: define permission constants (Countries.Manage, Currencies.Manage, Lookups.Manage, Applications.View, Applications.Review, Applications.AttachResults, Orders.View, Wallet.Credit, Wallet.Refund, Roles.Manage, AdminUsers.Manage, AuditLog.View) and a PermissionAuthorizationHandler.

Email service behind IEmailSender with an SMTP implementation and a dev "log to console" implementation. Integration tests: register → email content contains valid credentials → login succeeds → setup enforces country/currency pairing.
```

---

### 📋 Prompt 3 — Lookup & Cascading APIs + Admin CRUD Endpoints

```
Implement all lookup endpoints.

Public (applicant, filtered by the Order's VerificationCountry automatically from the JWT):
- GET /countries (active), GET /countries/{id}/currencies
- GET /identity-types
- GET /transaction-types            → filtered by order's country
- GET /transaction-types/{id}/sub-types
- GET /sub-types/{id}/authorities  → filtered by sub-type + order's country
- GET /authorities/{id}/service-types → includes Cost, ExpressCost, EnableExpress, ExecutionTimeDays, Description, RequiredFiles[]

Admin CRUD (guarded by permissions) for: Countries, Currencies, CountryCurrencies mapping, IdentityTypes, TransactionTypes (with CountryId), SubTransactionTypes, VerificationAuthorities (+ mapping to SubTransactionTypes), ServiceTypes (+ RequiredFiles management).

All lookup DTOs return NameAr and NameEn plus a resolved Name based on Accept-Language. Add paging/search to admin list endpoints. Integration tests must prove the cascade: changing country changes transaction types; an authority never appears for a sub-type/country pair it isn't mapped to.
```

---

### 🧾 Prompt 4 — Application Aggregate: Create / Edit / Delete + Files

```
Implement the Application feature (CQRS commands/queries).

Commands:
- CreateApplication (Draft) with: AddressedTo (الطلب موجه إلي), BirthDate, ApplicationNames (Arabic + English rows; First & Last required in both, Middle optional), ApplicationIdentities[] (IdentityTypeId, CardNumber, IssueDate, ExpiryDate; multiple allowed; ExpiryDate > IssueDate), TransactionTypeId, SubTransactionTypeId, VerificationAuthorityId, ApplicationServices[] (ServiceTypeId, Quantity, LanguageCode, IsExpress — reject IsExpress when ServiceType.EnableExpress is false). Server recomputes UnitCost/ExpressCost/LineTotal/TotalCost from DB prices — never trust client totals. Validate the whole cascade chain matches the order's country.
- UpdateApplication / DeleteApplication → only when CanEdit()/CanDelete() (unpaid). Return 409 with a clear ProblemDetails code otherwise.
- UploadApplicationFile (multipart): accept only pdf/jpg/jpeg/png, max 5 MB, verify magic bytes not just extension, store via IFileStorage (local disk implementation, path pattern /storage/orders/{orderId}/applications/{appId}/), link to ServiceTypeRequiredFile when provided.
- SubmitApplication → Draft → PendingPayment, requires all mandatory files present.

Queries:
- GetMyApplications (list with status, payment status, totals),
- GetApplicationDetails (full aggregate incl. services summary table data: ServiceName, Description, ExecutionTime, Cost, EnableExpress, ExpressCost, files, and ONLY ForUser comments).

Cover with integration tests: cascade validation, express rejection, file type/size rejection, edit-after-payment rejection.
```

---

### 💰 Prompt 5 — Wallet, Multi-Application Payment & Refund

```
Implement wallet and payments.

- GET /api/v1/orders/me/wallet → balance + paged ledger.
- POST /api/v1/payments { applicationIds[] } → pay MULTIPLE applications in one atomic transaction: validate all belong to the caller's order, all are PendingPayment, sum totals, debit wallet once (single WalletTransaction Type=Payment with ReferenceApplicationIds), set each application → Pending + PaidAtUtc. Use a DB transaction + optimistic concurrency so double-submit cannot double-charge. Insufficient balance → 422 with required vs available amounts.
- POST /api/v1/applications/{id}/refund → allowed ONLY when status = Pending; credits wallet (Type=Refund), status → Refunded, history entry. Any other status → 409.
- Admin: POST /api/v1/admin/orders/{id}/wallet/credit (permission Wallet.Credit) to top up, and admin-initiated refund endpoint (Wallet.Refund) with the same Pending-only rule.

Unit + integration tests: pay 3 apps together, partial-failure rolls back everything, refund blocked for InProgress/MissedInfo/Success/Failed, ledger always reconciles to balance.
```

---

### 💬 Prompt 6 — Review Workflow, Dual-Visibility Comments & Timeline

```
Implement the admin review workflow and the comment system.

- Admin endpoints (permission Applications.Review): change status Pending → InProgress; InProgress → Success or Failed; add comment { body, visibility: ForUser | Internal }. Posting a ForUser comment while InProgress sets status → MissedInfo automatically.
- Applicant endpoints: reply with a comment (always ForUser) and re-upload files while in MissedInfo; a resubmit action moves MissedInfo → InProgress.
- CRITICAL: Internal comments must be filtered out in the query layer for all applicant endpoints — add an integration test that proves an Internal comment never appears in any applicant response payload.
- GET timeline endpoint (both realms, filtered by visibility): merges ApplicationComments, ApplicationStatusHistory, and file upload events into one chronological feed with authorType, timestamps, and event kind — this powers the chat-style UI.
- On Success: admin uploads result files (Kind = AdminResult, permission Applications.AttachResults); applicant GET /applications/{id}/results lists downloadable files with secure, order-scoped download URLs.
- Write every action to the audit log.
```

---

### 🌍 Prompt 7 — Web App: i18n Shell, Registration & Order Setup

```
In apps/web build the applicant shell and onboarding.

- i18next with 7 locales: ar, en, ru, ur, de, hi, zh (lazy-loaded JSON in packages/i18n). Language switcher in the header; persist in localStorage and URL prefix /{lang}/. For ar and ur set dir="rtl" on <html> and use Tailwind logical properties (ms-*, me-*, ps-*, pe-*) so the entire layout mirrors correctly.
- Pages: Landing → Register (email only, success screen "check your email"), Login (OrderNumber + Password, prefilled order from deep link ?order=), Order Setup (Verification Country select → Currency select filtered by country; show wallet currency confirmation).
- Auth handling: JWT in memory + refresh pattern, TanStack Query client with auth interceptor from packages/api-client, route guards.
- Polished, modern design using packages/ui (shadcn/ui): clean typography for Arabic (e.g., IBM Plex Sans Arabic) and Latin scripts, proper number/date localization via Intl.
- Seed translation files with real translations for all UI strings in all 7 languages (use professional wording, not machine-garbled text).
```

---

### 🧙 Prompt 8 — Web App: Application Wizard

```
In apps/web build the "Create New Application" multi-step wizard (React Hook Form + Zod, one schema per step, state kept in a wizard store):

Step 1: AddressedTo input, placeholder "الطلب موجه إلي" (localized).
Step 2: Personal info — Arabic name (First/Middle/Last) + English name (First/Middle/Last), First & Last required in both; BirthDate picker (past dates only); repeatable Identity sections: IdentityType select (from API) → CardNumber, IssueDate, ExpiryDate with add/remove.
Step 3: Cascading selects — TransactionType → SubTransactionType → VerificationAuthority → repeatable service rows: ServiceType, Number (quantity), Language, Express switch shown ONLY when the selected ServiceType has enableExpress. Changing any parent resets all children (and shows a confirm if data would be lost).
Step 4: Services summary table: ServiceName, Description, ExecutionTime, Cost, Enable Express, Express Cost, Line Total, grand total in the order currency; plus required-files checklist per service.
Step 5: File upload with drag & drop — accept only pdf/jpg/jpeg/png, max 5 MB, instant client validation, progress bars, retry, mandatory-file gating before submit.
Step 6: Review everything → Save Draft or Submit (→ PendingPayment).

Fully RTL-compatible, mobile-responsive, with clear step indicator and per-step validation errors localized in all 7 languages.
```

---

### 💳 Prompt 9 — Web App: Applications List, Multi-Pay, Refund & Timeline

```
In apps/web build the Order dashboard:

- Applications table: ApplicationNumber, AddressedTo, status badge (Draft, PendingPayment, Pending, InProgress, MissedInfo, Success, Failed, Refunded — distinct colors), payment status, total.
- Row actions driven by server-provided capability flags (canEdit, canDelete, canRefund) — never infer on the client: Edit/Delete only when unpaid; Refund only when Pending; nothing when InProgress/MissedInfo/Success/Failed.
- Multi-select checkboxes on unpaid applications → sticky "Pay selected (total)" bar → confirmation dialog showing wallet balance vs total → pay together in one call; handle insufficient-balance error with a friendly message.
- Wallet page: balance + ledger (TopUp / Payment / Refund) with references.
- Application details page with tabs: Details, Services table, Files, and a chat-style Activity Timeline: two-sided bubbles (admin left / applicant right, mirrored in RTL), status-change entries as centered system chips, file events with icons, timestamps localized. MissedInfo state shows a prominent banner + reply composer + re-upload area + Resubmit button.
- When status = Success: a Results section listing admin-attached files with download buttons.
```

---

### 🛠️ Prompt 10 — Admin Panel

```
In apps/admin build the admin panel (Arabic + English, RTL-aware):

- Login, layout with permission-aware sidebar (hide modules the user lacks permission for; server still enforces).
- Dashboard: counters by status, revenue, recent activity.
- Lookups module: CRUD screens for Countries, Currencies, Country↔Currency mapping, Identity Types, Transaction Types (country-scoped), Sub-Transaction Types, Verification Authorities (+ sub-type mapping), Service Types (cost, express cost, EnableExpress, execution time, description, required files editor). Use a reusable DataTable (server pagination, search, sort) and a reusable CRUD drawer/dialog pattern.
- Orders module: search by OrderNumber/email; order detail with applications, wallet (credit + refund actions guarded by permissions), and ledger.
- Applications queue: filterable table; detail page with tabs Details / Files (preview pdf & images inline) / Comments — with a visibility toggle (For User / Internal) on the composer and clear visual distinction (e.g., internal = amber background + lock icon) / Timeline. Status action buttons follow the state machine; Success flow opens a result-files uploader.
- Roles & Permissions: role CRUD with grouped permission checkboxes; Admin Users CRUD with role assignment.
- Audit log viewer with filters.
```

---

### ✅ Prompt 11 — Hardening, Tests & Docs

```
Finalize the Data Verification platform:

1. Backend: add integration test suite covering the full happy path (register → login → setup → create app → upload → submit → pay 2 apps together → admin InProgress → ForUser comment (MissedInfo) → applicant reply + resubmit → Success → result download) and the forbidden paths (edit after pay, refund after InProgress, internal comment leakage, express on non-express service, wrong file type/size, currency not in country).
2. Security pass: authorize every endpoint, order-scoping on all applicant resources (an order can never read another order's data — add tests), file download authorization, rate limiting on register/login, request size limits, antiforgery for uploads, no stack traces in ProblemDetails.
3. Frontend: error boundaries, empty states, loading skeletons, form autosave in the wizard, e2e smoke test (Playwright) for the happy path in both LTR (en) and RTL (ar).
4. Docs: update README with architecture diagram, ERD, environment variables, seeding, and a "demo script" walking through the full flow with the seeded NEN client and Egypt lookups.
```

---

## 8. Suggested Delivery Plan

| Sprint | Scope |
|---|---|
| 1 | Prompts 0–2: scaffold, domain, auth, email |
| 2 | Prompts 3–4: lookups + application aggregate + files |
| 3 | Prompts 5–6: wallet, multi-pay, refunds, review workflow, comments |
| 4 | Prompts 7–8: web app shell, i18n, wizard |
| 5 | Prompts 9–10: dashboard, payments UI, admin panel |
| 6 | Prompt 11: hardening, tests, docs, UAT with NEN data |

---

*End of brief — Data Verification v1.0*
