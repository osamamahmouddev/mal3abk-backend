# Production Rollout & Rollback Plan — Malabak (ملعبك)

This runbook outlines the deployment procedures, migration bundles, DNS cutover, instant rollback mechanisms, and post-launch monitoring gates for Malabak per **[TASK-810]**.

---

## 1. Hosting Architecture & Target Environments

```
   [ Client Traffic ]
          |
          v
   [ Cloudflare DNS & WAF / Proxied ]
          |
          +---> malabak.com (Landing)      ===> Vercel (Next.js Edge/SSR)
          +---> venue.malabak.com (Venue)  ===> Vercel (Next.js SSR / BFF)
          +---> admin.malabak.com (Admin)  ===> Vercel (Next.js SSR / BFF)
          |
          +---> api.malabak.com            ===> MonsterASP.NET (IIS / ASP.NET Core 10 Web API)
                                                      |
                                                      +---> PostgreSQL (db76963.databaseasp.net:5432)
                                                      +---> Upstash Redis (TLS 6379)
```

---

## 2. Pre-Rollout Quality Gates

Before any production deployment, verify all criteria pass:
- [x] Backend builds with zero warnings: `dotnet build -warnaserror` (Exit Code 0).
- [x] All unit, security, and architecture tests pass: `dotnet test Malabak.sln` (Exit Code 0).
- [x] Zero vulnerable NuGet packages: `dotnet list Malabak.sln package --vulnerable --include-transitive`.
- [x] Web monorepo builds cleanly: `cd web && pnpm build` (All 3 apps: landing, venue, admin).
- [x] Zero vulnerable NPM packages: `cd web && pnpm audit --audit-level=high`.
- [x] Code formatting verified: `dotnet format --verify-no-changes`.

---

## 3. Backend Deployment: MonsterASP.NET

The backend runs out-of-process on IIS under MonsterASP.NET hosting.

### 3.1 Build the Publish Artifact
```bash
# 1. Generate framework-dependent release binaries
dotnet publish src/Malabak.Api/Malabak.Api.csproj \
  -c Release \
  -o monsterasp_publish \
  /p:UseAppHost=false

# 2. Package into deployment ZIP
cd monsterasp_publish
zip -r ../malabak-api-monsterasp.zip ./*
cd ..
```

### 3.2 Automated Startup Migration Execution
MonsterASP.NET does not offer SSH shell access for CLI migration commands. Malabak handles database schema evolution automatically on boot:
- In `monsterasp_publish/web.config`:
  ```xml
  <environmentVariable name="Database__MigrateOnStartup" value="true" />
  ```
- When the ASP.NET Core host boots, `DatabaseMigrator.MigrateAllAsync()` automatically applies pending EF Core migrations (`20261108200642_WaitlistEntries.cs`, etc.) to `db76963.databaseasp.net:5432` before incoming HTTP traffic is accepted.

### 3.3 Zero-Downtime Folder Swap on MonsterASP.NET
1. Connect via FTP or MonsterASP.NET Web File Manager.
2. Upload `malabak-api-monsterasp.zip` to a staging folder: `/site/deployments/v1.0.X/`.
3. Extract files into the new directory.
4. Update IIS Virtual Directory physical path or swap folder contents to `/site/wwwroot/`.
5. Ping `/health/ready` to verify successful startup and migration execution.

---

## 4. Web Frontend Deployment: Vercel

The web monorepo hosts three distinct Next.js applications:
1. `@malabak/landing` (`web/apps/landing` -> `malabak.com`)
2. `@malabak/venue-dashboard` (`web/apps/venue` -> `venue.malabak.com`)
3. `@malabak/admin-dashboard` (`web/apps/admin` -> `admin.malabak.com`)

### 4.1 Deployment Commands
```bash
# Deploy to Vercel via CLI (or automated via Git push to dev / main branches)
cd web
pnpm turbo build

# Deploy specific application
vercel --prod --cwd apps/landing
vercel --prod --cwd apps/venue
vercel --prod --cwd apps/admin
```

### 4.2 Mandatory Vercel Environment Variables
| Variable | Value / Description | Used In |
| :--- | :--- | :--- |
| `NEXT_PUBLIC_API_URL` | `https://api.malabak.com` | Landing, Venue, Admin |
| `API_URL` | `https://api.malabak.com` (BFF server-to-server proxy) | Venue, Admin |
| `IRON_SESSION_SECRET` | 32+ character random secret for secure cookie sealing | Venue, Admin |
| `NEXT_PUBLIC_CLOUDFLARE_TURNSTILE_SITE_KEY` | Turnstile public widget key | Landing, Venue |

---

## 5. Rollback Procedures (< 5 Minutes)

### 5.1 Backend Rollback on MonsterASP.NET
1. Open MonsterASP.NET File Manager or FTP.
2. Re-point IIS physical root to previous known-good deployment folder `/site/deployments/v1.0.PREV/`.
3. If migration rollback is required:
   - Run rollback SQL script against `db76963.databaseasp.net`.
4. Touch `web.config` to trigger immediate IIS worker process recycling.
5. Verify `/health/ready` returns HTTP 200 OK.

### 5.2 Frontend Rollback on Vercel
1. Open Vercel Project Dashboard (`Deployments` tab).
2. Locate the previous successful deployment.
3. Click the three dots `...` -> **Instant Rollback**.
4. Traffic is routed to the previous immutable serverless build within **5 seconds** without cache invalidation delays.

---

## 6. Post-Launch Smoke Test & Monitoring Verification

Immediately following rollout, perform smoke tests:
1. **Health Probes:**
   ```bash
   curl -s -i https://api.malabak.com/health/live | grep "200 OK"
   curl -s -i https://api.malabak.com/health/ready | grep "Healthy"
   ```
2. **Waitlist Smoke Test:**
   ```bash
   curl -X POST https://malabak.com/api/waitlist \
     -H "Content-Type: application/json" \
     -d '{"phone":"01099887766","audience":"Player","plan":"Starter","turnstileToken":"DEV_BYPASS"}'
   ```
3. **Seq / Serilog Exception Monitoring:**
   - Filter: `@Level in ['Error', 'Fatal'] and not(RequestPath like '/health%')`
   - Target: Zero unhandled 500 exceptions in the first 60 minutes post-release.
