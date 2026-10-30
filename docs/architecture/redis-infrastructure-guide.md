# Redis Infrastructure & Caching Architecture Guide

## 1. Overview & Architectural Role

In the **Malabak (ملعبك)** platform, Redis acts as a high-performance, in-memory distributed data store supporting multiple critical capabilities across all modules:

1. **Distributed Concurrency & Slot Locking:** Preventing double-booking race conditions across horizontally scaled application instances.
2. **Atomic Single-Use Token Storage:** Secure storage and atomic consumption for high-risk authentication credentials (OTP codes, WebSocket connection tickets, support impersonation codes).
3. **Session Validation & Revocation:** Fast lookup of active session identifiers (`sid`) during JWT validation to support instantaneous logout-all and account suspension.
4. **Distributed Rate Limiting:** Enforcing global rate limits across multiple API host instances behind Cloudflare.
5. **Fail-Secure (Fail-Closed) Security Controls:** Strict adherence to `SECURITY.md §1 & §7`: if Redis is degraded or unreachable, sensitive operations reject access rather than failing open.

```
+-------------------------------------------------------------------------+
|                              Malabak.Api                                |
|   +-------------------+  +-------------------+  +--------------------+  |
|   | IDistributedLock  |  | IAtomicConsume    |  |  RedisHealthCheck  |  |
|   |     Provider      |  |      Store        |  |  (Latency & Conn)  |  |
|   +---------+---------+  +---------+---------+  +---------+----------+  |
|             |                      |                      |             |
|             +----------------------+----------------------+             |
|                                    |                                    |
|                      FailClosedPolicy / LogMasking                      |
|                                    |                                    |
|                   StackExchange.Redis Multiplexer                       |
|                   (TLS 1.2/1.3, Password, Timeouts)                     |
+------------------------------------+------------------------------------+
                                     |
                                     v
                       +---------------------------+
                       |   Redis 7.2 Container     |
                       | (Private Network / VPC)   |
                       +---------------------------+
```

---

## 2. Key Naming Conventions (`IRedisKeyBuilder`)

To prevent key collision across modules and environments, all Redis keys must follow the standardized format:

```text
{prefix}:{environment}:{module}:{category}:{identifiers...}
```

- **Prefix:** Default `malabak`
- **Environment:** Normalized lowercase (e.g. `development`, `staging`, `production`)
- **Module:** Bound context name (e.g. `identity`, `booking`, `venues`, `ratelimit`)
- **Category:** Specific domain purpose (e.g. `otp`, `ws-ticket`, `lock`, `impersonation`, `ip`)
- **Identifiers:** Unique identifiers (e.g. phone number, pitch GUID, slot timestamp)

### Standard Generated Keys:
| Purpose | Key Pattern | Example |
|---|---|---|
| **OTP Verification** | `malabak:{env}:identity:otp:{purpose}:{phone}` | `malabak:dev:identity:otp:login:+201012345678` |
| **WebSocket Ticket** | `malabak:{env}:identity:ws-ticket:{jti}` | `malabak:dev:identity:ws-ticket:9a4b2c1d` |
| **Impersonation Code** | `malabak:{env}:identity:impersonation:{code}` | `malabak:dev:identity:impersonation:f831e4ab` |
| **Slot Booking Lock** | `malabak:{env}:booking:lock:slot:{pitchId}:{yyyyMMddHHmm}` | `malabak:dev:booking:lock:slot:c9b0e1...:202610281800` |
| **Rate Limit Counter** | `malabak:{env}:ratelimit:{category}:{identifier}` | `malabak:dev:ratelimit:ip:197.34.21.9` |

### Key Validation Rules:
- Segments must not be null, empty, or whitespace.
- Segments must not contain whitespace (` `, `\t`, `\r`, `\n`).
- Logging key sanitization: Phone numbers and sensitive codes are automatically masked (`***MASKED***`) in audit and diagnostic logs.

---

## 3. Distributed Locking (`IDistributedLockProvider`)

When players attempt to hold pitch slots simultaneously, database transactions alone can suffer lock contention or connection pool exhaustion under heavy traffic. `IDistributedLockProvider` provides lightweight, fast distributed locking.

### Implementation Details:
1. **Acquisition:**
   - Generates a unique cryptographic lock token (`Guid.CreateVersion7().ToString("N")`).
   - Uses Redis `SET resource token NX PX <expiry_ms>`.
   - If `timeout > TimeSpan.Zero`, executes a backoff retry loop with randomized jitter (10-30ms) until acquired or deadline expires.
2. **Safe Atomic Release (Lua Script):**
   Releasing the lock executes the following atomic Lua script to guarantee that a worker only deletes the lock if it still holds the original token:
   ```lua
   if redis.call('get', KEYS[1]) == ARGV[1] then
       return redis.call('del', KEYS[1])
   else
       return 0
   end
   ```
   *Benefit:* If a long-running process exceeds the lock's TTL and a second worker acquires it, the first worker will **not** accidentally release the second worker's lock when it eventually finishes.
3. **Async Disposal:**
   `IDistributedLock` implements `IAsyncDisposable`. Using the C# `await using` construct guarantees prompt release upon completing the critical section.

---

## 4. Atomic Single-Use Token Store (`IAtomicConsumeStore`)

Single-use tokens (OTPs, WebSocket tickets, customer support impersonation codes) are susceptible to replay attacks if reading and deletion are not performed in an atomic operation.

### Atomic Get-and-Delete (Lua Script):
```lua
local val = redis.call('get', KEYS[1])
if val then
    redis.call('del', KEYS[1])
end
return val
```
Because Redis executes Lua scripts atomically on a single thread:
- The token is retrieved and destroyed in a single network round-trip.
- Concurrent requests racing on the exact same token will result in exactly one request receiving the value and all others receiving `null`.

---

## 5. Fail-Closed vs. Fail-Open Security Directives

Adhering to **SECURITY.md §1 (Fail Secure)**:

| Operation Classification | Behavior on Redis Outage / Timeout | Rationale |
|---|---|---|
| **Sensitive (Fail-Closed)**<br>• Slot locking<br>• OTP consumption<br>• WS ticket validation<br>• Impersonation code consumption<br>• Sensitive route rate limits | **Rejects Request**<br>Throws `RedisFailClosedException`<br>(HTTP 503 or 401/429) | Never allow double bookings or unverified security tokens to proceed when the validation backend is down. |
| **Non-Sensitive (Fail-Open)**<br>• Venue search cache<br>• Pitch metadata cache<br>• Public announcements | **Falls back to Primary DB**<br>Logs warning and queries PostgreSQL | Gracefully degrades user browsing experience without compromising security or consistency. |

---

## 6. Connection Resiliency, TLS & Health Checks

1. **TLS Configuration:**
   - Every environment, local development included, connects to Redis over TLS (`ssl=true`). Locally, `docker compose` runs Redis with the plain-text port disabled (`--port 0 --tls-port 6379`), bound to `127.0.0.1` only.
   - `SslProtocols` enforced to TLS 1.2 and TLS 1.3.
   - The server certificate is always validated. For a server whose CA is not in the system trust store (the local dev CA from `scripts/generate-dev-certs.sh`, or a private cloud CA), set `Redis:TlsCaCertificatePath` to that CA's PEM file; startup fails if the file is missing. Never disable certificate validation instead.
   - Local certificates live in the git-ignored `docker/redis/tls/` (EC P-256, valid 825 days; regenerate with `--force`).
   - There is exactly one `IConnectionMultiplexer`, built by `RedisConnectionFactory`. Modules must not open their own connections: the Identity OTP store uses the shared one and has no in-memory fallback, so OTP fails closed when Redis is unavailable (§5).
2. **Lifecycle Event Monitoring:**
   - `ConnectionFailed`, `ConnectionRestored`, `ErrorMessage`, and `InternalError` events are monitored and logged to Serilog with structured metadata.
3. **Health Check (`RedisHealthCheck`):**
   - Verified on `/health/ready`.
   - Checks `IConnectionMultiplexer.IsConnected` and measures `PingAsync()` round-trip latency.
   - If latency exceeds 500ms, marks status as `Degraded` to alert DevOps before total disconnection.
