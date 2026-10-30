# Runbook: Staging Deployment on MonsterASP.NET (mal3abk.runasp.net)

This runbook guides deploying the Malabak ASP.NET Core backend to **MonsterASP.NET** (IIS on Windows Server) under the free subdomain `mal3abk.runasp.net` (Site: `site103465`).

---

## 1. Prerequisites (External Cloud Services)

MonsterASP.NET is an application host for .NET / IIS. Because Malabak relies on **PostgreSQL 17** and **Redis 7.2**, you must connect the deployed API to cloud instances of each (both have permanent free tiers):

### A. PostgreSQL on Neon (Free Tier)
1. Register at [neon.tech](https://neon.tech) and create a project in the region closest to Egypt (e.g. Frankfurt / EU Central).
2. Under **Connection Details**, select the **Direct connection** (not Pooled).
3. The connection string format for Npgsql:
   ```text
   Host=<endpoint>;Database=<dbname>;Username=<user>;Password=<password>;SSL Mode=Require;Trust Server Certificate=true
   ```

### B. Redis on Upstash (Free Tier)
1. Register at [upstash.com](https://upstash.com) and create a Redis database in Frankfurt (EU).
2. Ensure TLS/SSL is enabled.
3. The connection string format for StackExchange.Redis:
   ```text
   <endpoint>:6379,password=<password>,ssl=true,abortConnect=false
   ```

---

## 2. Configuration & Environment Settings

On MonsterASP.NET, configuration is passed through `appsettings.Production.json` or directly in `web.config` under `<environmentVariables>`.

### Required Keys:
- `ASPNETCORE_ENVIRONMENT`: `Staging` (enables `/swagger` at `https://mal3abk.runasp.net/swagger`)
- `Database:MigrateOnStartup`: `true` (automatically applies EF Core migrations when the site starts)
- `Hangfire:BypassDashboardAuthInDevelopment`: `false`
- `ConnectionStrings:Postgres`: The Neon connection string
- `ConnectionStrings:Redis`: The Upstash connection string
- `Security:Jwt:PrivateKeyPem`: ES256 ECDSA private key (PEM format)
- `Security:Otp:HmacSecret`: 64-character hex secret (`openssl rand -hex 32`)
- `Security:StepUp:SecretKey`: 64-character hex secret (`openssl rand -hex 32`)

---

## 3. Deployment Methods

### Option A: Manual Upload via WebFTP (Easiest)
1. Compile and publish the release package:
   ```bash
   docker run --rm -v $(pwd):/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
     dotnet publish src/Malabak.Api/Malabak.Api.csproj -c Release -o /src/monsterasp_publish
   cd monsterasp_publish && zip -r ../malabak-api-monsterasp.zip . && cd ..
   ```
2. Log into MonsterASP.NET Control Panel, navigate to **Overview** -> click **WebFTP [OPEN]** (`https://webftp.monsterasp.net`).
3. Open the `\wwwroot` directory.
4. Upload `malabak-api-monsterasp.zip`.
5. Right-click the uploaded zip file and click **Extract** into `\wwwroot`.
6. Restart the site from the control panel.

### Option B: Deployment via FTP / SFTP Client (FileZilla / WinSCP)
- **Host / Server:** `site103465.siteasp.net`
- **Port:** `21` (FTP) or `22` (SFTP)
- **User:** `site103465`
- **Password:** Reveal the password from the dashboard (click the **Show** eye icon).
- **Target Folder:** `\wwwroot`

### Option C: Automated CI/CD via GitHub Actions
Add a GitHub Actions workflow `.github/workflows/deploy-monsterasp.yml` using `SamKirkland/FTP-Deploy-Action` to build on every push to `main` and deploy to MonsterASP.NET automatically.

---

## 4. Verification

Once deployed, verify:
1. **Health Check:** `https://mal3abk.runasp.net/health/ready` (should return HTTP 200 with Postgres & Redis status Healthy).
2. **Ping:** `https://mal3abk.runasp.net/api/v1/ping`
3. **Swagger UI:** `https://mal3abk.runasp.net/swagger` (accessible in `Staging` environment).
