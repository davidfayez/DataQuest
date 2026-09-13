# Run & Build Guide — API, Admin, Web

Everything needed to build and run the three pieces of the platform on a developer machine:

| Piece     | Project                            | Dev URL                 |
| --------- | ---------------------------------- | ----------------------- |
| **API**   | `backend/src/DataVerification.API` | <http://localhost:5088> |
| **Web**   | `frontend/apps/web` (applicant)    | <http://localhost:5173> |
| **Admin** | `frontend/apps/admin` (back office)| <http://localhost:5174> |

Both SPAs call the API through a Vite dev proxy on `/api`, which targets `http://localhost:5088`.
That is why the API must be started on port **5088** in development — see [Step 3](#3-run-the-api).

---

## 1. Prerequisites

| Tool     | Version tested | Check with       |
| -------- | -------------- | ---------------- |
| .NET SDK | 10.0.100       | `dotnet --version` |
| Node.js  | 24.x           | `node -v`        |
| pnpm     | 11.x           | `pnpm -v`        |
| Docker   | 28.x           | `docker -v`      |

If pnpm is missing: `corepack enable && corepack prepare pnpm@11 --activate`
EF Core CLI (only needed for manual migrations): `dotnet tool install --global dotnet-ef`

---

## 2. Start infrastructure (SQL Server + Mailpit)

From the repository root:

```bash
docker compose up -d db mailpit
```

- SQL Server 2022 → `localhost,1433`, user `sa`, password `Your_strong_Passw0rd`
- Mailpit (catches all outgoing mail) → SMTP `localhost:1025`, web UI <http://localhost:8025>

> ⚠️ **Read before running the API.** `backend/src/DataVerification.API/appsettings.json` ships with
> `ConnectionStrings:DefaultConnection` pointing at a **remote production database**
> (`Server=172.201.83.229;Database=VData_Prod`). In `Development` the API **migrates and seeds the
> database on boot** (`Program.cs`), including demo applications. Point it at your local container
> before the first run — see the next section.

### Local connection string + JWT key (one time)

`DataVerification.API.csproj` has **no `UserSecretsId`**, so initialise the store first:

```bash
cd backend/src/DataVerification.API
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Server=localhost,1433;Database=DataVerification;User Id=sa;Password=Your_strong_Passw0rd;TrustServerCertificate=True;MultipleActiveResultSets=true"
dotnet user-secrets set "Jwt:SigningKey" "<a 32+ byte random secret>"
```

User secrets override `appsettings.json` and are never committed. `init` adds a `UserSecretsId`
element to the csproj — commit that, it holds no secret.

**Alternative — a machine-local SQL Server instead of Docker.** If a SQL Server instance is already
running on the host, skip the containers and override per run with environment variables (nothing on
disk changes):

```bash
cd backend
ASPNETCORE_ENVIRONMENT=Development \
ConnectionStrings__DefaultConnection="Server=localhost;Database=DataVerification;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true" \
Jwt__SigningKey="<a 32+ byte random secret>" \
dotnet run --project src/DataVerification.API --urls "http://localhost:5088"
```

The double underscore is the .NET config separator for nested keys. PowerShell equivalent:

```powershell
$env:ConnectionStrings__DefaultConnection = "Server=localhost;Database=DataVerification;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true"
$env:Jwt__SigningKey = "<a 32+ byte random secret>"
dotnet run --project src/DataVerification.API --urls "http://localhost:5088"
```

Without Docker there is no Mailpit; `Smtp:Enabled` is already `false` in development, so credentials
are echoed in the API response and log instead.

---

## 3. Run the API

```bash
cd backend
dotnet run --project src/DataVerification.API --urls "http://localhost:5088"
```

`--urls` is required: `Properties/launchSettings.json` defaults to
`https://localhost:59441;http://localhost:59442`, but the Vite proxies and the API test suites all
expect **5088**. (Alternatively set `ASPNETCORE_URLS=http://localhost:5088`, or edit
`applicationUrl` in `launchSettings.json` once.)

- Swagger UI → <http://localhost:5088/swagger>
- Health check → `GET http://localhost:5088/api/v1/health` → `200 OK`
- On first boot in `Development` the schema is migrated and lookups + the super admin are seeded.

Seeded admin login: `admin@dataverification.local` / `Admin#12345`
(override with `Seed:SuperAdmin:Email` / `Seed:SuperAdmin:Password`).

### Build only

```bash
cd backend
dotnet restore
dotnet build                       # whole solution
dotnet build -c Release
```

### Publish (deployable output)

```bash
cd backend
dotnet publish src/DataVerification.API/DataVerification.API.csproj -c Release -o ./publish
```

### Docker image for the API

```bash
cd backend
docker build -t dataverification-api .
docker run -p 8080:8080 \
  -e "ConnectionStrings__DefaultConnection=<your connection string>" \
  -e "Jwt__SigningKey=<32+ byte secret>" \
  -v dv-storage:/app/storage \
  dataverification-api
```

The image runs in `Production`, binds to `$PORT` (fallback `8080`), and stores uploads in
`/app/storage` — mount a volume there so files survive a redeploy. In non-development environments
migrations run only if you set `Database__MigrateOnStartup=true` (and `Database__SeedOnStartup=true`
for lookups).

### Manual migrations (optional)

```bash
cd backend
dotnet ef database update  -p src/DataVerification.Infrastructure -s src/DataVerification.API
dotnet ef migrations add <Name> -p src/DataVerification.Infrastructure -s src/DataVerification.API
```

---

## 4. Install frontend dependencies (once)

The frontend is a single pnpm workspace — install from `frontend/`, never from an app folder.

```bash
cd frontend
pnpm install
```

Workspace members: `apps/web`, `apps/admin`, `packages/ui`, `packages/api-client`, `packages/i18n`.

---

## 5. Run Web and Admin

```bash
cd frontend

pnpm dev          # both apps in parallel
pnpm dev:web      # applicant app only → http://localhost:5173
pnpm dev:admin    # admin panel only   → http://localhost:5174
```

Equivalent per-app forms:

```bash
pnpm --filter @dv/web dev
pnpm --filter @dv/admin dev
```

Both dev servers use `strictPort: true`, so they fail loudly instead of hopping to another port if
5173/5174 are taken. Both proxy `/api` → `http://localhost:5088`, so no CORS setup is needed in
development.

### Pointing a SPA at a different API

Each app reads `VITE_API_BASE_URL`, defaulting to `/api/v1/` (i.e. the proxy). To hit a deployed
API directly, create `frontend/apps/web/.env.local` (or `apps/admin/.env.local`):

```
VITE_API_BASE_URL=https://api.example.com/api/v1/
```

That origin must also appear in the API's `Cors:AllowedOrigins`.

---

## 6. Build Web and Admin

```bash
cd frontend

pnpm build                       # every workspace package + both apps
pnpm --filter @dv/web build      # applicant app  → apps/web/dist
pnpm --filter @dv/admin build    # admin panel    → apps/admin/dist
```

`build` runs `tsc --noEmit && vite build`, so a type error fails the build.

Preview a production bundle locally:

```bash
pnpm --filter @dv/web preview
pnpm --filter @dv/admin preview
```

Both apps are SPAs and need a catch-all rewrite to `/index.html` on whatever host serves `dist/`
(the committed `vercel.json` files already do this for Vercel).

---

## 7. Quality gates

```bash
cd backend  && dotnet build && dotnet test
cd frontend && pnpm lint && pnpm typecheck && pnpm build
```

Full test layers:

```bash
cd backend  && dotnet test                            # 57 unit + 35 integration (needs SQL Server)
pwsh tests/api/run-all.ps1                            # 235 API checks (API must be running on 5088)
cd frontend && pnpm --filter @dv/i18n check:locales   # locale integrity
cd frontend && pnpm --filter @dv/web e2e              # Playwright, applicant app
cd frontend && pnpm --filter @dv/admin e2e            # Playwright, admin panel
```

First Playwright run only: `pnpm --filter @dv/web exec playwright install`.
Point integration tests at another database with `INTEGRATION_TESTS_CONNECTION`.
`tests/api/run-all.ps1` takes `-BaseUrl` if the API is not on `http://localhost:5088/api/v1`.

---

## 8. Cold start, end to end

```bash
# terminal 0 — infrastructure
docker compose up -d db mailpit

# terminal 1 — API  (after setting user secrets, see §2)
cd backend && dotnet run --project src/DataVerification.API --urls "http://localhost:5088"

# terminal 2 — both front ends
cd frontend && pnpm install && pnpm dev
```

Then open:

- Applicant app → <http://localhost:5173>
- Admin panel → <http://localhost:5174> (`admin@dataverification.local` / `Admin#12345`)
- Swagger → <http://localhost:5088/swagger>
- Mailpit → <http://localhost:8025>

---

## 9. Troubleshooting

| Symptom | Cause / fix |
| --- | --- |
| SPA calls return 404/`ECONNREFUSED` on `/api/v1/...` | API is not on 5088. Restart it with `--urls "http://localhost:5088"`. |
| `Port 5173 is already in use` | `strictPort` is on — free the port or change it in `apps/web/vite.config.ts`. |
| API fails at boot with a SQL login/timeout error | Container not up (`docker compose ps`), or the connection string still points at the remote server — set the user secret in §2. |
| Migrations ran against the wrong database | Development migrates **and seeds** on boot. Verify `dotnet user-secrets list` before the first `dotnet run`. |
| JWT errors / 500 on sign-in | `Jwt:SigningKey` still the placeholder — set a 32+ byte secret via user secrets. |
| No credentials email | `Smtp:Enabled` is `false` in development; credentials are echoed in the API response and log. Start Mailpit and set `Smtp:Enabled=true` to receive mail at <http://localhost:8025>. |
| Uploads rejected | Extension **and** magic bytes must match, 5 MB cap: `.pdf`, `.jpg`, `.jpeg`, `.png`. |
| `pnpm` resolution errors in an app folder | Run `pnpm install` from `frontend/`, not from `apps/web` or `apps/admin`. |
