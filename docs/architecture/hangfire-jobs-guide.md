# Hangfire Background Processing & Job Architecture Guide

## 1. Overview & Architectural Role

In the **Malabak (ملعبك)** platform, asynchronous background processing is critical for decoupling heavy, failure-prone, or latency-inducing operations from user-facing HTTP request/response lifecycles.

Key workloads executed via Hangfire:
1. **Outbox Message Dispatch:** Reliable polling and publishing of domain events across modules.
2. **Notification Delivery:** SMS (OTP fallback), WhatsApp, Push Notifications, and transactional emails.
3. **Payment Expiration & Reconciliation:** Auto-canceling unpaid reservations after temporary holds expire (e.g., 10 minutes), webhook reconciliation with payment gateways (Paymob/Fawry).
4. **Maintenance & Cleanup:** Stale idempotency keys, expired temporary tokens, and archived audit logs.

```
+-------------------------------------------------------------------------+
|                               Malabak.Api                               |
|   +-------------------+  +-------------------+  +--------------------+  |
|   |   Named Queues    |  |  Exponential      |  | Secured Dashboard  |  |
|   | (crit/def/low)    |  |  Backoff + Jitter |  | (Admin + CF Access)|  |
|   +---------+---------+  +---------+---------+  +---------+----------+  |
|             |                      |                      |             |
|             +----------------------+----------------------+             |
|                                    |                                    |
|                      IdempotentBackgroundJob<TArgs>                     |
|                                    |                                    |
|                   Hangfire.PostgreSql Storage Engine                    |
|                   (Schema: hangfire, Invisibility timeout)              |
+------------------------------------+------------------------------------+
                                     |
                                     v
                       +---------------------------+
                       |    PostgreSQL Database    |
                       |     (Dedicated Schema)    |
                       +---------------------------+
```

---

## 2. PostgreSQL Storage Engine (`Hangfire.PostgreSql`)

Hangfire is configured to use the primary PostgreSQL database with a dedicated `hangfire` schema to ensure isolation from module domain schemas.

### Storage Configuration Highlights:
- **Schema:** `hangfire` (auto-prepared on startup if necessary).
- **Queue Poll Interval:** Configurable (default `15 seconds`).
- **Invisibility Timeout:** `5 minutes` to protect against hung workers.
- **Distributed Locks:** PostgreSQL transactional locks managed by Hangfire driver to coordinate leader election and server heartbeats.

```json
"Hangfire": {
  "SchemaName": "hangfire",
  "WorkerCount": 10,
  "Queues": ["critical", "default", "low"],
  "DashboardPath": "/hangfire",
  "EnableDashboard": true,
  "QueuePollIntervalSeconds": 15
}
```

---

## 3. Queue Topology & Workload Isolation

To prevent low-priority maintenance tasks or bulk notification sweeps from starving time-sensitive operations, Hangfire workers process queues strictly in priority order:

| Queue | Priority | Workloads |
|---|---|---|
| `critical` | **Highest** | Payment reconciliation, slot reservation expiry cancellations, urgent OTP SMS fallbacks |
| `default` | **Medium** | Domain event outbox dispatch, booking confirmation push notifications, email receipts |
| `low` | **Lowest** | Maintenance cleanup, audit log archiving, daily analytical rollups |

### Worker Dispatch Configuration:
```csharp
services.AddHangfireServer(options =>
{
    options.WorkerCount = hangfireOptions.WorkerCount;
    options.Queues = [
        HangfireQueues.Critical,
        HangfireQueues.Default,
        HangfireQueues.Low
    ];
});
```

---

## 4. Exponential Backoff & Jitter Retry Policy

Transient failures (e.g., third-party payment gateway downtime, temporary SMS vendor rate limits, network blips) must not cause immediate permanent failure or thunderous herd retries.

The global filter `HangfireRetryPolicyFilterAttribute` overrides Hangfire's default retry behavior:
- **Maximum Retry Attempts:** Default 5 attempts (configurable).
- **Exponential Backoff Formula:**
  $$\text{delay} = \min(10 \times 2^{\text{attempt}-1} + \text{jitter}, 1800\text{s})$$
- **Jitter Range:** $0$ to $5$ seconds of cryptographic random jitter to prevent retry storms across parallel failing jobs.
- **Maximum Delay Cap:** $30$ minutes.
- **Exceeded Retries:** When retry count reaches the maximum threshold, the job is transitioned directly to `FailedState`, and a failure metric is recorded to OpenTelemetry.

---

## 5. Idempotent Background Job Pattern

In distributed systems, background jobs can be re-executed due to worker restarts, transient timeouts, or manual re-queuing. Under `SECURITY.md §8` and `rule.md`, all background jobs MUST be strictly idempotent.

### Implementation Pattern:
Derive all jobs from `IdempotentBackgroundJob<TArgs>`:

```csharp
public class ExpireHoldJob(
    IDistributedLockProvider lockProvider,
    IBookingService bookingService,
    ILogger<ExpireHoldJob> logger)
    : IdempotentBackgroundJob<ExpireHoldArgs>(lockProvider, logger)
{
    protected override TimeSpan LockExpiry => TimeSpan.FromMinutes(2);

    protected override string BuildLockKey(ExpireHoldArgs args) =>
        $"malabak:prod:booking:lock:job:expire-hold:{args.HoldId}";

    protected override async Task ExecuteIdempotentAsync(
        ExpireHoldArgs args,
        PerformContext context,
        CancellationToken cancellationToken)
    {
        await bookingService.ExpireHoldAsync(args.HoldId, cancellationToken);
    }
}
```

### Safety Guarantees:
1. **Concurrency Lock:** Ensures two workers never process the same domain entity concurrently.
2. **Lock Expiration:** Auto-releases if a worker crashes mid-execution.
3. **Clean Skip:** If another worker currently holds the lock, the job gracefully yields rather than clashing.

---

## 6. Secured Dashboard (`HangfireDashboardAuthorizationFilter`)

The Hangfire web dashboard (`/hangfire`) provides deep operational inspection and manual job triggering capabilities. Exposing it without strict defense-in-depth violates `SECURITY.md §2`.

### Security Requirements:
1. **Authenticated Session:** User must have a valid JWT with an active session.
2. **Strict Admin Role:** User must possess `RoleNames.Admin`.
3. **Cloudflare Access Zero Trust Header (Optional / Staging & Prod):** When `RequireCloudflareAccess` is configured, verifies that the `Cf-Access-Jwt-Assertion` header is present.
4. **Deny-by-Default:** Any unauthenticated request, non-admin user, or missing Cloudflare header returns HTTP 401/403 or dashboard denial.
5. **Environment Gating:** Dashboard can be disabled entirely via `Hangfire:EnableDashboard: false`.

---

## 7. Observability & Telemetry

Hangfire operations emit structured metrics via OpenTelemetry (`MeterName: "Malabak.Hangfire"`):

- `malabak.hangfire.jobs.executed`: Counter for total jobs processed, tagged with `job_name` and `queue`.
- `malabak.hangfire.jobs.failed`: Counter for failed jobs, tagged with `job_name`, `queue`, and `exception_type`.
- `malabak.hangfire.jobs.duration`: Histogram tracking execution duration in milliseconds.

The dashboard and health check endpoint (`/healthz`) monitor PostgreSQL connectivity and Hangfire server heartbeats.
