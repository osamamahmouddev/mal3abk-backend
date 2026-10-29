# ADR 0003: Field-Level Encryption and Key Management

- **Status:** Accepted
- **Date:** 2026-10-28
- **Context:**
  SECURITY.md §9.4–9.5 requires AES-256-GCM field encryption with AAD for the most sensitive columns (venue payout accounts, wallets, InstaPay handles, sensitive support notes, later phone/email), HMAC blind indexes so encrypted phone/email stay searchable, envelope encryption with the KEK held in a KMS / Key Vault, key rotation with re-encryption, and a Data Protection key ring that survives redeploys and is shared by all instances. The hosting provider (and therefore the KMS) is still an open decision (rule.md §14, item 3).

- **Decision:**
  1. **Abstractions in `Malabak.SharedKernel.Security`:** `IFieldCipher`, `IBlindIndexService`, `FieldEncryptionContext`, `IReEncryptionTarget`. Modules depend only on these; implementations live in `Malabak.Api/Infrastructure/Security`.
  2. **Ciphertext format:** `version(1) | dataKeyId(4, BE) | nonce(12) | tag(16) | ciphertext`, stored as `bytea`. AAD = header + `table|column|rowId`, so a value can't be moved to another row/column and the key id can't be swapped.
  3. **Envelope keys:** data keys (DEKs) are random 256-bit keys stored only wrapped in `security.data_encryption_keys`; exactly one is active (filtered unique index). The key ring is loaded lazily, cached in memory and refreshed every `KeyRingRefreshMinutes` so rotations by other nodes are picked up.
  4. **KEK behind `IKeyEncryptionKeyProvider`.** Until hosting is decided, `LocalKeyEncryptionKeyProvider` reads KEKs from the secret store (User Secrets locally, environment / secret manager in staging and production). Several KEKs can be configured at once for rotation.
  5. **Blind index:** HMAC-SHA256 with its own key (never a KEK), domain-separated by purpose (`phone`, `email`). Callers normalise first (E.164, lower-case email).
  6. **Data Protection:** the key ring is persisted in `security.data_protection_keys` and every key element is envelope-encrypted under the current KEK.
  7. **Rotation:** `EncryptionKeyRotationService` rotates the DEK, re-wraps DEKs after a KEK change and drives each module's `IReEncryptionTarget` in batches. All steps are idempotent so they can run as Hangfire recurring jobs (wired up with TASK-105).
  8. **Fail closed:** outside Development, startup fails (`ValidateOnStart`) if keys are missing or malformed. Development without configured keys gets random in-memory keys and a warning, instead of a hardcoded fallback secret.

- **Alternatives Considered:**
  - *EF Core value converters for transparent encryption:* rejected. AAD needs the row id, converters can't be async, and transparent decryption makes accidental logging/serialisation of plaintext easy.
  - *Data Protection API for field data:* rejected. It isn't designed for long-lived data at rest, and its key expiry and rotation model doesn't fit re-encryption.
  - *Picking Azure Key Vault or AWS KMS now:* deferred until the hosting ADR, so we don't take on a cloud SDK that may be thrown away.

- **Consequences:**
  - **Interim risk:** until a KMS-backed `IKeyEncryptionKeyProvider` is added, the KEK lives in process memory and in the secret store. Anyone with both database access and the app's secrets can decrypt. This must be closed before production launch (SECURITY.md S4).
  - Each module that adds encrypted columns implements `IReEncryptionTarget` for its own tables and registers it in DI.
  - Blind-index key rotation means recomputing every index column; it has no automated path yet.
  - Operators must back up KEKs separately from database backups. A lost KEK means the data is lost.
