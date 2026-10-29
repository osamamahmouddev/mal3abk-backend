# Secret & Credential Rotation Runbook — Malabak (ملعبك)

This runbook outlines mandatory step-by-step procedures for rotating production and staging credentials across Malabak infrastructure adhering to **[TASK-103]** and **`SECURITY.md §16–§17`**.

---

## 1. Routine Rotation Schedule
| Credential / Secret | Rotation Frequency | Primary Location | Max Permitted Lifetime |
| :--- | :---: | :--- | :---: |
| **ES256 JWT Signing Keys** | **Every 90 Days** | Secret Manager / Key Vault | 180 Days |
| **PostgreSQL Database Password** | **Every 180 Days** | Secret Manager & DB Roles | 365 Days |
| **Redis Cache Password** | **Every 180 Days** | Secret Manager & Redis Config | 365 Days |
| **Cloudflare Turnstile Secret Key** | **Every 180 Days** | Cloudflare Dashboard & App Settings | 365 Days |
| **External SMS / WhatsApp API Tokens** | **Every 90 Days** | Provider Portal & App Settings | 180 Days |

---

## 2. Zero-Downtime Secret Rotation Procedures

### 2.1 ES256 JWT Signing Key Rotation (Graceful Migration via `kid`)
The Malabak JWT subsystem uses Key IDs (`kid`) to decouple signature generation from validation. This enables key rotation without logging out active users.

1. **Generate a New NIST P-256 Keypair:**
   ```bash
   openssl ecparam -name prime256v1 -genkey -noout -out ec-p256-key-v2.pem
   openssl ec -in ec-p256-key-v2.pem -pubout -out ec-p256-key-v2.pub.pem
   ```
2. **Assign a New `kid`:** e.g., `malabak-es256-key-v2`.
3. **Phase 1 — Add New Key to Key Vault as Validation Key:**
   - Deploy backend with both `v1` (Current Signing Key) and `v2` (Validation Key).
   - Verify all hosts recognize both public keys for signature validation.
4. **Phase 2 — Promote New Key to Active Signing Key:**
   - Update `Security:Jwt:CurrentKeyId` to `malabak-es256-key-v2`.
   - Update `Security:Jwt:PrivateKeyPem` with the new private key.
   - All newly issued access tokens will now be signed with `v2`.
5. **Phase 3 — Retire Old Key (After Grace Period):**
   - Access tokens have a maximum lifetime of 15 minutes.
   - Wait at least **30 minutes** after Phase 2 promotion to ensure all `v1`-signed access tokens have naturally expired.
   - Remove `v1` from the allowed validation keys array.

---

### 2.2 PostgreSQL Database Password Rotation (Dual-Role Rollover)
Never change a database password in-place if it causes service downtime. Always follow the dual-user rollover pattern:

1. **Create an Alternate User in PostgreSQL:**
   ```sql
   CREATE USER malabak_app_b WITH PASSWORD 'new_strong_generated_password_here';
   GRANT malabak_app TO malabak_app_b; -- Inherits all table/schema grants
   ```
2. **Update Application Configuration in Secret Manager:**
   - Point connection string to user `malabak_app_b` with the new password.
   - Perform a rolling restart of the API host containers.
3. **Verify Health Checks:**
   - Check `/health/ready` returns `"postgres": "Healthy"`.
   - Check Seq logs for zero database authentication errors.
4. **Drop / Decommission the Old User:**
   - Wait 24 hours to ensure no background batch jobs or stale workers are using the old credentials.
   ```sql
   REVOKE ALL PRIVILEGES ON ALL TABLES IN SCHEMA identity, venues, booking FROM malabak_app_a;
   DROP USER malabak_app_a;
   ```

---

### 2.3 Redis Cluster Password Rotation
1. **Set Secondary Password in Redis:**
   Redis supports dual passwords via ACL (Access Control Lists) or `requirepass`:
   ```bash
   redis-cli -a current_password CONFIG SET requirepass "new_redis_password"
   ```
2. **Update App Configuration:**
   - Update `Redis:ConnectionString` in Secret Manager.
   - Rolling restart backend hosts.
3. **Verify:**
   - Verify `/health/ready` reports Redis healthy latency.

---

### 2.4 Cloudflare Turnstile & External Messaging Keys
1. **Generate a new Secret Key in the Cloudflare / VictoryLink / Twilio dashboard.**
2. **Update Secret Manager / Environment Variables:**
   - Set `MALABAK_Security__Turnstile__SecretKey` or update Secret Manager.
3. **Trigger Rolling Deployment.**
4. **Revoke old API key from the provider portal.**

---

## 3. Emergency Secret Compromise Protocol (Incident Response)
If an API key, database password, or private key is leaked (e.g. accidentally pushed to public git or exposed in logs):

1. **Trigger Incident Response (SECURITY.md §17.1):** Notify Security Lead immediately.
2. **Revoke and Rotate the Compromised Secret Immediately:**
   - If **JWT Private Key** is leaked:
     - Immediately generate a new key and update `Security:Jwt:CurrentKeyId`.
     - Invalidate all active user sessions by calling `RevokeAllSessions` (updating `User.SecurityStamp`).
     - This forces all mobile and web clients to re-authenticate via fresh OTP.
   - If **Database Password** is leaked:
     - Change the password in PostgreSQL immediately (`ALTER USER malabak WITH PASSWORD '...'`).
     - Terminate active rogue connections:
       ```sql
       SELECT pg_terminate_backend(pid) FROM pg_stat_activity 
       WHERE usename = 'malabak' AND client_addr != 'app_server_ip';
       ```
3. **Audit Inspection:**
   - Query `audit.*` logs in PostgreSQL and Seq logs for the duration of the exposure window to inspect any unauthorized data access or modifications.
4. **Post-Mortem & Preventative Action:**
   - Update `.gitleaks.toml` rules.
   - Conduct retrospective and file post-mortem report in `docs/runbooks/postmortems/`.
