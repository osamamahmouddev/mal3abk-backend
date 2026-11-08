# Incident Response Plan & Emergency Controls — Malabak (ملعبك)

This document specifies the operational incident response protocols, severity taxonomy, emergency kill-switches, secret rotation procedures, communication templates, and blameless post-mortem standards for the Malabak platform per **[TASK-805]** and **`SECURITY.md §16`**.

---

## 1. Incident Severity Levels & SLAs

| Severity | Description | Target Response | Target Resolution | Escalation Cadence |
| :--- | :--- | :---: | :---: | :---: |
| **SEV-1 (Critical)** | Active data breach, total platform outage, JWT private key compromise, payment webhook forging, unauthorized database access. | **< 15 mins** | **< 2 hours** | Every 30 mins to Leadership |
| **SEV-2 (High)** | Degradation of core booking/payment flow, intermittent SignalR failures, partial venue dashboard lockouts, elevated 5xx rate (> 2%). | **< 30 mins** | **< 4 hours** | Every 60 mins |
| **SEV-3 (Medium)** | Minor feature dysfunction (e.g. avatar uploads failing, waitlist CSV export slow), non-critical third-party timeout. | **< 2 hours** | **< 24 hours** | Daily update |
| **SEV-4 (Low)** | Minor UI cosmetic glitch, copy error, analytics tracking mismatch. | **< 1 business day** | Next release cycle | Weekly sync |

---

## 2. Incident Response Team & Contacts

| Role | Responsibility | Primary Channel |
| :--- | :--- | :--- |
| **Incident Commander (IC)** | Directs response, delegates tasks, manages timeline, decides Sev level. | Slack `#incident-war-room` / Signal |
| **Technical Lead** | Coordinates debugging, hotfixes, database/infra emergency operations. | Voice bridge / Debug session |
| **Communications Lead** | Prepares user-facing notices, customer support messaging, venue updates. | Arabic / English messaging |
| **Infrastructure / DBA** | Manages MonsterASP.NET, PostgreSQL `db76963`, Upstash Redis, Vercel deployments. | Server consoles / Dashboards |

---

## 3. Emergency Controls & Drills

### 3.1 Emergency Revoke All Sessions Drill
When an attacker gains unauthorized session persistence or refresh token leak is suspected:

1. **Trigger Mass Revocation in Database:**
   ```sql
   -- Mark all refresh tokens as revoked immediately
   UPDATE identity.refresh_tokens
   SET is_revoked = TRUE,
       revoked_at_utc = NOW(),
       revocation_reason = 'EMERGENCY_SECURITY_INCIDENT'
   WHERE is_revoked = FALSE;

   -- Bump global user security stamp / token validity epoch
   UPDATE identity.users
   SET security_stamp = gen_random_uuid(),
       updated_at_utc = NOW();
   ```

2. **Evict Distributed Redis Session Cache:**
   ```bash
   # Flush all active session keys from Redis
   redis-cli -u "$REDIS_URL" --scan --pattern "session:*" | xargs redis-cli -u "$REDIS_URL" DEL
   redis-cli -u "$REDIS_URL" --scan --pattern "wsjti:*" | xargs redis-cli -u "$REDIS_URL" DEL
   ```

3. **Verify:** Ensure any existing access/refresh token exchange fails with `401 Unauthorized` (`token_revoked`).

---

### 3.2 Emergency JWT Key Compromise Drill
If the active ES256 private key is compromised:

1. **Immediate Invalidation (Bypass 30-min Grace Period):**
   - Instantly remove the compromised `kid` from `Security:Jwt:AllowedValidationKeys`.
   - Generate a new P-256 keypair:
     ```bash
     openssl ecparam -name prime256v1 -genkey -noout -out ec-p256-emergency.pem
     openssl ec -in ec-p256-emergency.pem -pubout -out ec-p256-emergency.pub.pem
     ```
   - Update `Security:Jwt:CurrentKeyId` to `malabak-es256-emergency-01`.
   - Deploy backend to MonsterASP.NET.
2. **Force Token Re-issue:**
   - Because the compromised key is dropped from validation keys, all existing JWT tokens signed with it are rejected instantly (zero second propagation).
   - Valid users will automatically use refresh tokens to obtain newly signed tokens or re-authenticate via OTP.

---

### 3.3 Emergency Venue Kill-Switch
If a venue owner account is hijacked or suspect fraudulent pitch pricing is detected:

1. **Lock the Venue via Admin API / Database:**
   ```sql
   UPDATE venues.venues
   SET status = 'Suspended',
       updated_at_utc = NOW()
   WHERE id = 'TARGET_VENUE_UUID';
   ```
2. **Revoke Venue Staff Sessions:**
   ```sql
   UPDATE identity.refresh_tokens
   SET is_revoked = TRUE, revoked_at_utc = NOW()
   WHERE user_id IN (
       SELECT user_id FROM venues.venue_staff WHERE venue_id = 'TARGET_VENUE_UUID'
   );
   ```

---

## 4. Arabic Incident Communication Templates (قوالب التواصل بالعربية)

### قالب 1: إشعار توقف مؤقت للصيانة الطارئة (للأفراد / اللاعبين)
> **تنبيه أمني من منصة ملعبك**  
> مرحباً بك،  
> نود إبلاغك بأنه تم رصد تحديث أمني طارئ، وقمنا باتخاذ إجراء احترازي سريع بإعادة تعيين جلسات الدخول على المنصة لضمان أمان حسابات جميع اللاعبين.  
> إذا تم تسجيل خروجك من الحساب، يمكنك إعادة تسجيل الدخول بسهولة عبر إدخال رقم هاتفك ورمز التأكيد (OTP).  
> نؤكد لك أن جميع حجوزاتك وبياناتك المالية مشفرة ومحفوظة بالكامل دون أي مساس بها.  
> فريق دعم ملعبك — support@malabak.com

### قالب 2: إشعار لأصحاب الملاعب والشركاء (حالات تأخر تأكيد الدفع أو توقف الخدمة)
> **تحديث تشغيلي هام — شركاء ملاعب ملعبك**  
> السادة أصحاب الملاعب المحترمين،  
> نود إحاطتكم علماً بوجود تأخير تقني مؤقت في معالجة إشعارات التحويلات الإلكترونية (فودافون كاش / إنستاباي) نظراً لأعمال صيانة طارئة على شبكة الربط.  
> نرجو منكم اعتماد إيصالات التحويل البنكي يدوياً من لوحة التحكم في حال الضرورة.  
> فرقنا الفنية تعمل على مدار الساعة لإنهاء التحديث خلال أقل من [ساعة واحدة].  
> للاستفسارات العاجلة: الخط الساخن لدعم الملاعب متاح لديكم.

---

## 5. Blameless Post-Mortem Template

```markdown
# Incident Post-Mortem: [TITLE] (SEV-X)

## Executive Summary
- **Date:** YYYY-MM-DD
- **Duration:** X hours Y minutes
- **Incident Commander:** [Name]
- **Impact:** [Summary of affected users, venues, or financial transactions]

## Timeline (Cairo Time - UTC+2)
- **HH:MM** — Anomaly detected by [Seq Alert / Health check / User report]
- **HH:MM** — Incident declared as SEV-X by [IC]
- **HH:MM** — Root cause identified in [Component/Endpoint]
- **HH:MM** — Mitigation deployed: [Actions taken]
- **HH:MM** — System health fully restored and verified
- **HH:MM** — Incident closed

## Root Cause Analysis (5 Whys)
1. Why did the issue occur? ...
2. Why did that happen? ...
3. Why was it not caught earlier? ...
4. Why did existing tests not cover it? ...
5. Root cause: ...

## What Went Well
- Automated alert fired within X minutes.
- Rate limiters prevented unauthorized scraping.

## Where We Got Lucky
- Low traffic volume during late night hours.

## Corrective Action Items
| Action Item | Type | Owner | Target Date |
| :--- | :---: | :---: | :---: |
| Add integration test for edge case | Prevent | [Dev] | YYYY-MM-DD |
| Tune alert threshold in Seq | Detect | [Dev] | YYYY-MM-DD |
```
