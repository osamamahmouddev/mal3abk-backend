# Malabak Operational Runbooks & Playbooks (فهرس أدلة التشغيل)

This directory houses the authoritative operations runbooks, disaster recovery protocols, incident response playbooks, and venue onboarding documentation for the **Malabak (ملعبك)** platform per **[TASK-813]**.

---

## 1. Deployment & Hosting Operations

| Document | Scope | Target Platform |
| :--- | :--- | :--- |
| **[Production Rollout & Rollback](production-rollout-rollback.md)** | Multi-stage production release gates, automated startup migrations, atomic directory swap, 5-minute rollback procedures. | MonsterASP.NET (API) & Vercel (Web) |
| **[MonsterASP.NET Deployment](monsterasp-staging.md)** | Windows/IIS out-of-process hosting, `web.config` environment mappings, and database connections. | MonsterASP.NET |
| **[Cloudflare Access & Edge Security](cloudflare-access.md)** | Cloudflare WAF, Turnstile bot challenges, origin cloaking, Zero Trust admin access, and Rate Limiting. | Cloudflare Edge |
| **[Email Sending Domain Setup](email-sending-domain.md)** | SPF, DKIM, DMARC, and Resend/SendGrid DNS configuration for `malabak.com`. | DNS / Email Delivery |
| **[Fly.io Staging Deployment](fly-staging.md)** | Staging container deployment guide. | Fly.io Containers |

---

## 2. Security, Business Continuity & Disaster Recovery

| Document | Scope | Key Protocols |
| :--- | :--- | :--- |
| **[Incident Response Plan](incident-response.md)** | Incident severity definitions (SEV-1 to SEV-4), emergency session revocation (`revoke-all-sessions`), JWT private key hot-swap, venue emergency kill-switch, and Arabic communication templates. | Incident Commander Playbook |
| **[Secret & Credential Rotation](secret-rotation-runbook.md)** | Zero-downtime rotation for ES256 signing keys (`kid`), PostgreSQL passwords, Redis auth, and provider API tokens. | Security Baseline |
| **[PostgreSQL Disaster Recovery & Backups](database-disaster-recovery.md)** | 3-2-1 backup architecture, encrypted S3 archiving, Point-In-Time Recovery (PITR), GiST constraint integrity validation, and off-site key escrow. | DBA / DevOps |

---

## 3. Pilot Venue & Business Operations

| Document | Scope | Language |
| :--- | :--- | :---: |
| **[دليل تشغيل وانضمام الملاعب التجريبية](pilot-venue-onboarding-ar.md)** | دليل كامل لأصحاب ومسؤولي الملاعب: تسجيل الدخول عبر OTP، إدارة الملاعب والأسعار، مراجعة إيصالات التحويل (فودافون كاش / إنستاباي)، الحجوزات اليدوية، واستيراد البيانات السابقة. | العربية |
| **[Staging User Acceptance Testing (Flows A–G)](uat-pilot-flows.md)** | Verification matrix and sign-off report across all PRD flows (Venue Onboarding, Discovery, Slot Hold, Walk-in, Proof Review, Cancellation, Settlement). | English / AR |
