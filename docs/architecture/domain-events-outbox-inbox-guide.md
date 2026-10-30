# Domain Events, Outbox & Inbox Architecture Guide

## 1. Overview & Architectural Role

In the **Malabak (ملعبك)** platform, modules are strictly isolated boundaries (`SECURITY.md §1 & §4`, `rule.md §1`). Direct cross-module database writes or synchronous coupling across modules are forbidden. Cross-module side effects—such as transactional notifications, audit logs, rankings/stats recomputation, and real-time SignalR broadcasts—must occur via **Domain Events** with guaranteed delivery.

To resolve the dual-write problem across distributed databases and external brokers, Malabak implements the **Outbox Pattern** with **Inbox Deduplication**:

```
+----------------------------------------------------------------------------------------------------+
|                                         Aggregate / Unit of Work                                   |
|   1. Mutation: booking.Confirm()                                                                   |
|   2. RaiseDomainEvent(new BookingConfirmedEvent(booking.Id))                                       |
+----------------------------------------------------------------------------------------------------+
                                                  |
                                                  v
+----------------------------------------------------------------------------------------------------+
|                                 EF Core OutboxInterceptor (Atomic TX)                              |
|   BEGIN TRANSACTION                                                                                |
|     INSERT INTO booking.bookings ...                                                               |
|     INSERT INTO booking.outbox_messages (id, type, content, occurred_on_utc)                       |
|   COMMIT TRANSACTION                                                                               |
+----------------------------------------------------------------------------------------------------+
                                                  |
                                                  v
+----------------------------------------------------------------------------------------------------+
|                           Outbox Dispatcher (Background / Hangfire)                                |
|   - Redis Distributed Lock: malabak:{env}:outbox:lock:{source}                                     |
|   - Query unprocessed messages (WHERE processed_on_utc IS NULL)                                    |
|   - IDomainEventDispatcher -> Handlers                                                             |
|   - Exponential Backoff + Jitter on failure                                                        |
+----------------------------------------------------------------------------------------------------+
                                                  |
                                                  v
+----------------------------------------------------------------------------------------------------+
|                               Idempotent Consumer (Inbox Deduplication)                            |
|   - Check inbox_messages (event_id, handler_name)                                                  |
|   - If processed -> skip duplicate execution                                                       |
|   - If new -> execute handler body + record in inbox_messages                                      |
+----------------------------------------------------------------------------------------------------+
```

---

## 2. Domain Events Abstraction

All domain events implement `IDomainEvent` or inherit from `DomainEvent`:

```csharp
public abstract record DomainEvent : IDomainEvent
{
    public Guid EventId { get; init; } = IdGenerator.NewGuid(); // Guid v7
    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
```

### Raising Events in Aggregates:
Entities inheriting from `Entity<TId>` implement `IHasDomainEvents`. Domain events are recorded in-memory inside the aggregate and dispatched upon persisting changes:

```csharp
public sealed class Booking : Entity<Guid>
{
    public void Confirm(DateTime confirmedAtUtc)
    {
        Status = BookingStatus.Confirmed;
        RaiseDomainEvent(new BookingConfirmedEvent(Id, PitchId, PlayerId, TotalAmount));
    }
}
```

---

## 3. Transactional Outbox Pattern (`OutboxInterceptor`)

The `OutboxInterceptor` derives from EF Core's `SaveChangesInterceptor`. During `SavingChangesAsync`:
1. It scans `ChangeTracker.Entries<IHasDomainEvents>()` for entities with pending domain events.
2. For each event, it serializes the payload using `System.Text.Json` into an `OutboxMessage` record.
3. It adds the `OutboxMessage` to the module's `outbox_messages` table in EF Core.
4. It clears domain events from the entity (`ClearDomainEvents()`).
5. EF Core commits the entity mutations and the outbox records inside the **same atomic database transaction**.

### Guarantees:
- **No Lost Events:** If the transaction commits, the event is guaranteed to be in the database.
- **No Phantom Events:** If the transaction rolls back due to a constraint violation or error, the outbox message is also rolled back.

---

## 4. Outbox Dispatcher with Exponential Backoff & Jitter

The outbox dispatcher periodically processes pending messages across module DbContexts.

### Concurrency Protection:
- Multiple API nodes or background workers coordinate using Redis distributed locks:
  `malabak:{env}:outbox:lock:{source_name}`
- If a worker cannot acquire the lock, it yields gracefully without clashing.

### Dispatching & Resiliency:
1. Messages are queried in batch: `WHERE processed_on_utc IS NULL AND (next_retry_utc IS NULL OR next_retry_utc <= now) ORDER BY occurred_on_utc`.
2. The event payload is deserialized and dispatched via `IDomainEventDispatcher` to registered handlers (`IDomainEventHandler<TEvent>`).
3. **Success:** `message.MarkAsProcessed(clock.UtcNow)` and OpenTelemetry `malabak.outbox.messages.dispatched` is incremented.
4. **Transient Failure:**
   - Increments `RetryCount`.
   - Computes exponential backoff with randomized jitter:
     $$\text{delay} = \min(2^{\text{attempt}} \times 2 + \text{jitter}, 1800\text{s})$$
   - Jitter ($0$ to $2$ seconds) prevents thundering herd retries against failing downstream services.
   - Sets `NextRetryUtc = clock.UtcNow + delay`.
5. **Dead-Letter Threshold:**
   - If `RetryCount >= MaxRetries` (default: 5), the message is dead-lettered (`NextRetryUtc = null`), an error log is emitted, and `malabak.outbox.messages.failed` is recorded with `is_dead_letter = true`.

---

## 5. Inbox Deduplication for Consumers

Because outbox dispatching guarantees **at-least-once delivery**, network timeouts, worker crashes, or retries could cause a domain event to be delivered more than once.

To achieve **exactly-once processing semantics**, consumers inherit from `IdempotentDomainEventHandler<TEvent>` and leverage `IInboxStore` (`EfInboxStore`):

```csharp
public sealed class SendBookingConfirmationNotificationHandler
    : IdempotentDomainEventHandler<BookingConfirmedEvent>
{
    public override string HandlerName => "Notifications.SendBookingConfirmationNotificationHandler";

    public SendBookingConfirmationNotificationHandler(
        IInboxStore inboxStore,
        ILogger<SendBookingConfirmationNotificationHandler> logger)
        : base(inboxStore, logger)
    {
    }

    protected override async Task HandleIdempotentAsync(
        BookingConfirmedEvent domainEvent,
        CancellationToken cancellationToken)
    {
        // Business logic executed AT MOST ONCE per EventId
        await notificationService.SendSmsAsync(...);
    }
}
```

### Table Structure (`inbox_messages`):
- `event_id`: UUID (Guid v7 from incoming event).
- `handler_name`: VARCHAR(256) (identifies the consumer).
- Composite Primary Key: `(event_id, handler_name)`.
- Multiple distinct consumers can independently process the same event without interfering with each other's deduplication state.

---

## 6. Observability & Telemetry

Outbox operations emit structured metrics under the OpenTelemetry meter `Malabak.Outbox`:

| Metric Name | Type | Description |
|---|---|---|
| `malabak.outbox.messages.dispatched` | Counter | Total outbox messages successfully delivered to handlers |
| `malabak.outbox.messages.failed` | Counter | Total outbox dispatch failures (tagged with `event_type`, `exception_type`, `is_dead_letter`) |
| `malabak.outbox.dispatch.duration` | Histogram | Execution time of outbox dispatch batches in milliseconds |
