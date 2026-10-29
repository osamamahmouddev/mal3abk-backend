## Description
<!-- Provide a clear, concise summary of the changes and motivation. -->

## Linked Task(s)
<!-- Example: Closes [TASK-001] or Relates to [TASK-102] -->
- Closes [TASK-XXX]

## Type of Change
- [ ] `feat`: New feature / business functionality
- [ ] `fix`: Bug fix
- [ ] `refactor`: Code refactoring without behavioral changes
- [ ] `test`: Unit, integration, or architecture tests
- [ ] `docs`: Documentation, runbooks, or ADRs
- [ ] `chore` / `build` / `ci`: Dependencies, tooling, workflows, build scripts
- [ ] `perf`: Performance improvement

## Definition of Done (DoD) Checklist
- [ ] Solution builds with zero compiler warnings (`dotnet build -c Release -warnaserror`).
- [ ] All unit, architecture, and integration tests pass green (`dotnet test -c Release`).
- [ ] Formatting verification passes cleanly (`dotnet format --verify-no-changes`).
- [ ] Web monorepo builds and typechecks pass (`turbo build` & `turbo typecheck`).
- [ ] Arabic (RTL primary) and English strings are defined in resource files (no hardcoded UI strings).
- [ ] ADR updated in `docs/adr/` if an architectural decision was introduced or modified.
- [ ] Database migration included if EF Core schema was changed, verified as safe and reversible.

## Security & Compliance Checklist (SECURITY.md)
- [ ] Deny-by-default enforced: all endpoints require authentication unless explicitly marked `.AllowAnonymous()`.
- [ ] Zero secrets in code: No API keys, passwords, OTPs, or private keys committed or logged.
- [ ] PII Masking: Sensitive fields (phone numbers, tokens, OTPs) are masked in structured logs.
- [ ] Server-side authorization & validation: Client input is validated with FluentValidation; authorization verified server-side.
- [ ] Error sanitization: Returns RFC 9457 `ProblemDetails` with correlation `traceId`; no stack traces leaked.
- [ ] Secret scan passes locally (`gitleaks detect`).

## Screenshots / Verification Output (if applicable)
<!-- Attach screenshots for UI/mobile changes or test runner output -->
