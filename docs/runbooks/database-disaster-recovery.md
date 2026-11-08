# PostgreSQL Disaster Recovery & Backup Restoration Runbook — Malabak (ملعبك)

This runbook outlines the **3-2-1 backup architecture**, Point-In-Time Recovery (PITR) procedures, offline key backup protocols, and disaster recovery validation drills for the Malabak database per **[TASK-806]**, **[TASK-072]**, and **`SECURITY.md §17`**.

---

## 1. Disaster Recovery Objectives & SLAs

| Metric | Target SLA | Description |
| :--- | :---: | :--- |
| **Recovery Point Objective (RPO)** | **< 15 minutes** | Maximum allowable data loss in catastrophic disaster. |
| **Recovery Time Objective (RTO)** | **< 30 minutes** | Maximum allowable downtime to restore full database operations. |
| **Data Integrity Gate** | **100% Zero-Loss** | Strict validation of GiST booking slot locks, financial outbox, and AES-GCM PII encryption tags. |

---

## 2. 3-2-1 Backup Architecture

```
                                  [ Production PostgreSQL ]
                             (MonsterASP.NET db76963 / Port 5432)
                                              |
                   +--------------------------+--------------------------+
                   |                                                     |
                   v                                                     v
        [ Copy 1: Local / Host ]                              [ Copy 2: Encrypted Off-Site ]
   Automated Hourly WAL Archiving +                      Nightly Encrypted pg_dump to Private S3
       MonsterASP Snapshot Backups                         Bucket with Object Lock (Immutability)
                   |                                                     |
                   +--------------------------+--------------------------+
                                              |
                                              v
                                   [ Copy 3: Cold Archive ]
                              Weekly GPG-Encrypted Deep Cold Storage
                              (Physical Offline / Multi-Region Cold)
```

1. **3 Copies of Data:** Primary DB, automated WAL/snapshot backups, off-site encrypted dumps.
2. **2 Different Media/Environments:** MonsterASP.NET managed storage + AWS S3 Private Encrypted Bucket.
3. **1 Copy Off-Site & Immutable:** S3 bucket configured with **Object Lock (Compliance Mode, 30 days retention)** to guarantee protection against ransomware or host compromise.

---

## 3. Automated Backup Procedure

### 3.1 Encrypted Dump Script (`scripts/backup-db.sh`)
```bash
#!/usr/bin/env bash
set -euo pipefail

TIMESTAMP=$(date -u +"%Y%m%d_%H%M%SZ")
BACKUP_DIR="/tmp/malabak_backups"
BACKUP_FILE="${BACKUP_DIR}/malabak_prod_${TIMESTAMP}.dump"
ENCRYPTED_FILE="${BACKUP_FILE}.enc"

mkdir -p "${BACKUP_DIR}"

echo "[INFO] Starting PostgreSQL backup for db76963..."
# Ensure DB authentication via .pgpass or standard environment configuration
pg_dump \
  -h "${DB_HOST:-db76963.databaseasp.net}" \
  -p "${DB_PORT:-5432}" \
  -U "${DB_USER:-db76963}" \
  -d "${DB_NAME:-db76963}" \
  -F c \
  -b \
  -v \
  -f "${BACKUP_FILE}"

echo "[INFO] Encrypting backup with OpenSSL AES-256-CBC..."
openssl enc -aes-256-cbc -salt -pbkdf2 -iter 100000 \
  -in "${BACKUP_FILE}" \
  -out "${ENCRYPTED_FILE}" \
  -pass env:BACKUP_ENCRYPTION_KEY

echo "[INFO] Uploading encrypted backup to off-site S3 cold storage..."
aws s3 cp "${ENCRYPTED_FILE}" "s3://${BACKUP_S3_BUCKET}/postgres/${TIMESTAMP}.dump.enc" \
  --sse aws:kms \
  --sse-kms-key-id "${BACKUP_KMS_KEY_ARN}"

# Clean up local temporary unencrypted artifacts
rm -f "${BACKUP_FILE}" "${ENCRYPTED_FILE}"
echo "[SUCCESS] Backup completed and securely shipped to S3."
```

---

## 4. Disaster Recovery Restoration Drill (Step-by-Step)

### Step 1: Provision Isolated Staging / DR Database
To execute a drill or recover from a failure:
1. Provision a target database or clean existing staging database:
   ```bash
   createdb -h staging-db.databaseasp.net -U db_admin malabak_recovery_drill
   ```

### Step 2: Retrieve & Decrypt the Snapshot
```bash
# 1. Download target backup from S3
aws s3 cp "s3://${BACKUP_S3_BUCKET}/postgres/20261108_200000Z.dump.enc" ./drill.dump.enc

# 2. Decrypt with backup key
openssl enc -d -aes-256-cbc -pbkdf2 -iter 100000 \
  -in ./drill.dump.enc \
  -out ./drill.dump \
  -pass env:BACKUP_ENCRYPTION_KEY
```

### Step 3: Execute Restoration via `pg_restore`
```bash
# Provide staging credentials via environment or .pgpass
pg_restore \
  -h staging-db.databaseasp.net \
  -p 5432 \
  -U db_admin \
  -d malabak_recovery_drill \
  --clean \
  --if-exists \
  --no-owner \
  --no-privileges \
  -v ./drill.dump
```

---

## 5. Post-Restore Data Integrity Verification Drill

After restoration, the Technical Lead must execute the following automated validation queries:

```sql
-- 1. Verify GiST Exclusion Constraint on confirmed booking slots exists and is active
SELECT conname, contype, conrelid::regclass
FROM pg_constraint
WHERE conname = 'bdr_no_overlapping_confirmed_slots';
-- Expected result: 1 row, contype = 'x' (Exclusion)

-- 2. Verify Schema Integrity across all Malabak modules
SELECT table_schema, count(*)
FROM information_schema.tables
WHERE table_schema IN ('identity', 'venues', 'booking', 'payments', 'subscriptions', 'teams', 'matches', 'audit', 'support')
GROUP BY table_schema
ORDER BY table_schema;
-- Expected result: All 9 schemas present with correct table counts

-- 3. Verify Outbox and Inbox messages count
SELECT status, count(*)
FROM booking.outbox_messages
GROUP BY status;

-- 4. Verify Waitlist and PII Blind Indexes
SELECT audience, count(*)
FROM identity.waitlist_entries
GROUP BY audience;

-- 5. Test GiST Constraint Enforcement (Attempt intentional double booking - Must Fail)
-- Must throw: SQLSTATE 23P01 (exclusion_violation)
```

---

## 6. Key Backup & Disaster Recovery Vault Procedure

Cryptographic keys must NEVER be stored inside the database or in the same cloud provider account as backups:

1. **Root KMS / AES Encryption Key Backup:**
   - Master field encryption keys (`Security:FieldEncryption:Key`) and HMAC blind index keys (`Security:BlindIndex:Key`) are backed up in an offline, air-gapped password manager vault (1Password / Bitwarden Enterprise Vault).
   - Access requires **MFA + Hardware Security Key (YubiKey)** approval from at least 2 designated key custodians.
2. **JWT Private Key Escrow:**
   - NIST P-256 private keys are stored in encrypted format in the Disaster Recovery Vault.
3. **Emergency Restore Access:**
   - In total catastrophe where the primary server is unreachable, new application hosts can be booted on alternative infrastructure by pulling configurations and keys from the Disaster Recovery Vault.
