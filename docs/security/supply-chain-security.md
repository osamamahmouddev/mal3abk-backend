# Supply Chain Security & Dependency Management Policy

## 1. Overview
This policy outlines the supply-chain defense mechanisms implemented across the Malabak platform (Backend .NET 10 solution and Next.js Web monorepo).

## 2. Dependency Ingestion & Integrity
1. **Frozen Lockfiles:**
   - In CI/CD and deployment environments, dependencies are restored strictly via lockfiles (`pnpm-lock.yaml` with `--frozen-lockfile`, and NuGet locked mode).
   - Direct modifying of lockfiles without automated tool generation is prohibited.
2. **Automated Vulnerability Scanning:**
   - **.NET Backend:** `dotnet list Malabak.sln package --vulnerable --include-transitive` is executed as a required quality gate in CI. Builds fail if high or critical CVEs are detected.
   - **Web Monorepo:** `pnpm audit --audit-level=high` is enforced on every commit and PR.
3. **Automated Dependency Updates (Dependabot):**
   - Configured in `.github/dependabot.yml`.
   - Checks weekly on Sundays at 03:00 Cairo time for `nuget`, `npm`, and `github-actions`.
   - Security advisories trigger automated pull requests with minimal version bumping.

## 3. Dependency Overrides & Remediation
When a transitive dependency introduces a vulnerability before an upstream package updates, Malabak enforces explicit overrides in `web/package.json`:
- `postcss`: Enforced `>= 8.5.23` to eliminate source map path traversal (CVE-2026-73646) and unescaped CSS stringify XSS.
- `playwright`: Enforced `>= 1.55.1` to eliminate unauthenticated SSL installer execution (CVE-2025-59288).
- Unused transitive dependencies are pruned immediately.

## 4. Software Bill of Materials (SBOM) Generation
SBOM generation is supported via industry-standard tooling:
- **.NET Ecosystem:**
  ```bash
  # Generate CycloneDX SBOM for .NET solutions
  dotnet tool install --global CycloneDX
  dotnet-cyclonedx Malabak.sln -o ./artifacts/sbom-dotnet.json --json
  ```
- **NPM / Web Ecosystem:**
  ```bash
  # Generate CycloneDX SBOM for Web monorepo
  cd web && npx @cyclonedx/cyclonedx-npm --output-file ../artifacts/sbom-web.json
  ```

## 5. Artifact Provenance & Secrets Scanning
- Every commit is audited via **Gitleaks** to prevent credentials, API keys, or tokens from entering repository history.
- Multi-stage Docker containers are built using minimal base images (`mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled`) running under non-root users (`UID 10001:appuser`).
