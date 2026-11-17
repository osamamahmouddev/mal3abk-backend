using Malabak.SharedKernel;

namespace Malabak.Modules.Subscriptions.Domain;

public enum BillingCycle
{
    Monthly = 0,
    Annual = 1
}

public enum SubscriptionStatus
{
    Trial = 0,
    Active = 1,
    Grace = 2,
    Expired = 3,
    Suspended = 4,
    Cancelled = 5
}

public enum SubscriptionEventType
{
    Assigned = 0,
    PlanChanged = 1,
    PaymentRecorded = 2,
    EnteredGrace = 3,
    Expired = 4,
    Suspended = 5,
    Reactivated = 6,
    Cancelled = 7,
    ExpiryWarning7Days = 8,
    ExpiryWarning1Day = 9
}

/// <summary>
/// A venue owner's subscription (TASK-708). Trial and Active periods end at <see cref="CurrentPeriodEndUtc"/>; then the
/// owner gets the plan's grace days, then the subscription expires. Expired, suspended and cancelled subscriptions keep
/// existing venues and bookings working but allow no new resources (see the entitlement service).
/// </summary>
public sealed class OwnerSubscription
{
    private readonly List<SubscriptionEvent> _events = [];

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public Guid PlanId { get; private set; }
    public BillingCycle Cycle { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public DateTime StartedAtUtc { get; private set; }
    public DateTime CurrentPeriodEndUtc { get; private set; }

    /// <summary>Start of the running period (the payment covering an unpaid period is invoiced from here).</summary>
    public DateTime CurrentPeriodStartUtc { get; private set; }

    /// <summary>Whether the running period has been paid (false for a plan assigned without a trial until the first payment).</summary>
    public bool CurrentPeriodPaid { get; private set; }
    public DateTime? GraceEndsAtUtc { get; private set; }
    public string? StatusReason { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public uint RowVersion { get; }
    public IReadOnlyList<SubscriptionEvent> Events => _events;

    private OwnerSubscription()
    {
    }

    /// <summary>Starts a subscription: a trial when the plan has trial days, otherwise active until one period from now.</summary>
    public static OwnerSubscription Start(Guid ownerId, SubscriptionPlan plan, BillingCycle cycle, bool withTrial, Guid actorId, DateTime nowUtc)
    {
        var trial = withTrial && plan.TrialDays > 0;
        var subscription = new OwnerSubscription
        {
            Id = IdGenerator.NewGuid(),
            OwnerId = ownerId,
            PlanId = plan.Id,
            Cycle = cycle,
            Status = trial ? SubscriptionStatus.Trial : SubscriptionStatus.Active,
            StartedAtUtc = nowUtc,
            CurrentPeriodStartUtc = nowUtc,
            CurrentPeriodEndUtc = trial ? nowUtc.AddDays(plan.TrialDays) : Extend(nowUtc, cycle),
            CurrentPeriodPaid = false,
            UpdatedAtUtc = nowUtc
        };
        subscription.Record(SubscriptionEventType.Assigned, actorId, nowUtc, toPlanId: plan.Id, note: trial ? $"Trial {plan.TrialDays} days" : null);
        return subscription;
    }

    /// <summary>Allows new resources only while the subscription is in good standing.</summary>
    public bool AllowsNewResources => Status is SubscriptionStatus.Trial or SubscriptionStatus.Active or SubscriptionStatus.Grace;

    public Result ChangePlan(SubscriptionPlan plan, BillingCycle cycle, Guid actorId, string? note, DateTime nowUtc)
    {
        if (Status == SubscriptionStatus.Cancelled)
        {
            return Result.Failure(SubscriptionErrors.Closed);
        }

        var from = PlanId;
        PlanId = plan.Id;
        Cycle = cycle;
        UpdatedAtUtc = nowUtc;
        Record(SubscriptionEventType.PlanChanged, actorId, nowUtc, fromPlanId: from, toPlanId: plan.Id, note: note);
        return Result.Success();
    }

    /// <summary>A manual payment covers the next period: from the current end (or now, if it already passed).</summary>
    /// <summary>
    /// Records a manual payment and returns the period it pays for. The first payment of a running, unpaid period (a plan
    /// assigned without a trial) pays that period; a payment while the period is already paid extends it by one cycle; a
    /// payment after a trial, grace or expiry starts a new period today.
    /// </summary>
    public (DateTime PeriodStart, DateTime PeriodEnd) RecordPayment(Guid actorId, string? note, DateTime nowUtc)
    {
        var running = Status is SubscriptionStatus.Active && CurrentPeriodEndUtc > nowUtc;
        DateTime start;
        DateTime end;
        if (running && !CurrentPeriodPaid)
        {
            start = CurrentPeriodStartUtc;
            end = CurrentPeriodEndUtc;
        }
        else if (running)
        {
            start = CurrentPeriodEndUtc;
            end = Extend(start, Cycle);
        }
        else
        {
            start = nowUtc;
            end = Extend(nowUtc, Cycle);
            CurrentPeriodStartUtc = nowUtc;
        }

        CurrentPeriodEndUtc = end;
        CurrentPeriodPaid = true;
        GraceEndsAtUtc = null;
        Status = SubscriptionStatus.Active;
        StatusReason = null;
        UpdatedAtUtc = nowUtc;
        Record(SubscriptionEventType.PaymentRecorded, actorId, nowUtc, note: note);
        return (start, end);
    }

    public Result Suspend(string reason, Guid actorId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(SubscriptionErrors.ReasonRequired);
        }

        if (Status is SubscriptionStatus.Suspended or SubscriptionStatus.Cancelled)
        {
            return Result.Failure(SubscriptionErrors.InvalidTransition);
        }

        Status = SubscriptionStatus.Suspended;
        StatusReason = reason.Trim();
        UpdatedAtUtc = nowUtc;
        Record(SubscriptionEventType.Suspended, actorId, nowUtc, note: StatusReason);
        return Result.Success();
    }

    /// <summary>Lifts a suspension: back to Active if the paid period is still running, otherwise Expired.</summary>
    public Result Reactivate(Guid actorId, DateTime nowUtc)
    {
        if (Status != SubscriptionStatus.Suspended)
        {
            return Result.Failure(SubscriptionErrors.InvalidTransition);
        }

        Status = CurrentPeriodEndUtc > nowUtc ? SubscriptionStatus.Active : SubscriptionStatus.Expired;
        StatusReason = null;
        UpdatedAtUtc = nowUtc;
        Record(SubscriptionEventType.Reactivated, actorId, nowUtc);
        return Result.Success();
    }

    public Result Cancel(string reason, Guid actorId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(SubscriptionErrors.ReasonRequired);
        }

        if (Status == SubscriptionStatus.Cancelled)
        {
            return Result.Failure(SubscriptionErrors.InvalidTransition);
        }

        Status = SubscriptionStatus.Cancelled;
        StatusReason = reason.Trim();
        UpdatedAtUtc = nowUtc;
        Record(SubscriptionEventType.Cancelled, actorId, nowUtc, note: StatusReason);
        return Result.Success();
    }

    /// <summary>
    /// Time-based transitions run by the daily job: period over → grace (when the plan has grace days) → expired.
    /// Returns the transition that happened, if any.
    /// </summary>
    public SubscriptionEventType? Advance(int graceDays, DateTime nowUtc)
    {
        if (Status is SubscriptionStatus.Trial or SubscriptionStatus.Active && CurrentPeriodEndUtc <= nowUtc)
        {
            if (graceDays > 0)
            {
                Status = SubscriptionStatus.Grace;
                GraceEndsAtUtc = CurrentPeriodEndUtc.AddDays(graceDays);
                UpdatedAtUtc = nowUtc;
                Record(SubscriptionEventType.EnteredGrace, null, nowUtc);
                // A sweep that runs late can find the grace period already over too.
                return GraceEndsAtUtc <= nowUtc ? ExpireNow(nowUtc) : SubscriptionEventType.EnteredGrace;
            }

            return ExpireNow(nowUtc);
        }

        return Status == SubscriptionStatus.Grace && GraceEndsAtUtc <= nowUtc ? ExpireNow(nowUtc) : null;
    }

    /// <summary>Expiry warning due now (7 days or 1 day before the period ends), unless already sent for this period.</summary>
    public SubscriptionEventType? DueWarning(DateTime nowUtc)
    {
        if (Status is not (SubscriptionStatus.Trial or SubscriptionStatus.Active))
        {
            return null;
        }

        var left = CurrentPeriodEndUtc - nowUtc;
        SubscriptionEventType due;
        if (left > TimeSpan.Zero && left <= TimeSpan.FromDays(1))
        {
            due = SubscriptionEventType.ExpiryWarning1Day;
        }
        else if (left > TimeSpan.FromDays(1) && left <= TimeSpan.FromDays(7))
        {
            due = SubscriptionEventType.ExpiryWarning7Days;
        }
        else
        {
            return null;
        }

        var periodStart = CurrentPeriodEndUtc.AddDays(-8);
        return _events.Exists(e => e.Type == due && e.OccurredAtUtc >= periodStart) ? null : due;
    }

    public void MarkWarningSent(SubscriptionEventType warning, DateTime nowUtc) => Record(warning, null, nowUtc);

    private SubscriptionEventType? ExpireNow(DateTime nowUtc)
    {
        Status = SubscriptionStatus.Expired;
        UpdatedAtUtc = nowUtc;
        Record(SubscriptionEventType.Expired, null, nowUtc);
        return SubscriptionEventType.Expired;
    }

    private void Record(SubscriptionEventType type, Guid? actorId, DateTime nowUtc, Guid? fromPlanId = null, Guid? toPlanId = null, string? note = null) =>
        _events.Add(new SubscriptionEvent(IdGenerator.NewGuid(), Id, type, fromPlanId, toPlanId, note, actorId, nowUtc));

    private static DateTime Extend(DateTime from, BillingCycle cycle) => cycle == BillingCycle.Annual ? from.AddYears(1) : from.AddMonths(1);
}

/// <summary>Subscription history: every change, payment, warning and automatic transition.</summary>
public sealed class SubscriptionEvent
{
    public Guid Id { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public SubscriptionEventType Type { get; private set; }
    public Guid? FromPlanId { get; private set; }
    public Guid? ToPlanId { get; private set; }
    public string? Note { get; private set; }
    public Guid? ActorId { get; private set; }
    public DateTime OccurredAtUtc { get; private set; }

    private SubscriptionEvent()
    {
    }

    public SubscriptionEvent(Guid id, Guid subscriptionId, SubscriptionEventType type, Guid? fromPlanId, Guid? toPlanId, string? note, Guid? actorId, DateTime occurredAtUtc)
    {
        Id = id;
        SubscriptionId = subscriptionId;
        Type = type;
        FromPlanId = fromPlanId;
        ToPlanId = toPlanId;
        Note = note?.Length > 500 ? note[..500] : note;
        ActorId = actorId;
        OccurredAtUtc = occurredAtUtc;
    }
}

public enum PaymentMethod
{
    Cash = 0,
    BankTransfer = 1,
    InstaPay = 2,
    MobileWallet = 3
}

/// <summary>A payment an admin recorded by hand (no online gateway in the MVP).</summary>
public sealed class SubscriptionPayment
{
    public Guid Id { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public Guid OwnerId { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "EGP";
    public PaymentMethod Method { get; private set; }
    public string? Reference { get; private set; }
    public DateTime PaidAtUtc { get; private set; }
    public DateTime PeriodStartUtc { get; private set; }
    public DateTime PeriodEndUtc { get; private set; }
    public Guid RecordedBy { get; private set; }
    public DateTime RecordedAtUtc { get; private set; }
    public string? Notes { get; private set; }

    private SubscriptionPayment()
    {
    }

    public static SubscriptionPayment Create(
        Guid subscriptionId, Guid ownerId, decimal amount, string currency, PaymentMethod method, string? reference, DateTime paidAtUtc,
        DateTime periodStart, DateTime periodEnd, Guid recordedBy, DateTime nowUtc, string? notes) => new()
        {
            Id = IdGenerator.NewGuid(),
            SubscriptionId = subscriptionId,
            OwnerId = ownerId,
            Amount = decimal.Round(amount, 2),
            Currency = currency,
            Method = method,
            Reference = reference?.Trim(),
            PaidAtUtc = paidAtUtc,
            PeriodStartUtc = periodStart,
            PeriodEndUtc = periodEnd,
            RecordedBy = recordedBy,
            RecordedAtUtc = nowUtc,
            Notes = notes?.Trim()
        };
}

public enum InvoiceStatus
{
    Paid = 0,
    Void = 1
}

/// <summary>Invoice issued for a recorded payment (TASK-709). Never deleted: a mistake is voided with a reason.</summary>
public sealed class Invoice
{
    public Guid Id { get; private set; }
    public string Number { get; private set; } = string.Empty;
    public Guid OwnerId { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public Guid PaymentId { get; private set; }
    public string PlanCode { get; private set; } = string.Empty;
    public string PlanName { get; private set; } = string.Empty;
    public BillingCycle Cycle { get; private set; }
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = "EGP";
    public DateTime IssuedAtUtc { get; private set; }
    public DateTime PeriodStartUtc { get; private set; }
    public DateTime PeriodEndUtc { get; private set; }
    public InvoiceStatus Status { get; private set; }
    public string? VoidReason { get; private set; }
    public DateTime? VoidedAtUtc { get; private set; }
    public Guid? VoidedBy { get; private set; }

    private Invoice()
    {
    }

    public static Invoice Issue(string number, SubscriptionPayment payment, SubscriptionPlan plan, BillingCycle cycle, DateTime nowUtc) => new()
    {
        Id = IdGenerator.NewGuid(),
        Number = number,
        OwnerId = payment.OwnerId,
        SubscriptionId = payment.SubscriptionId,
        PaymentId = payment.Id,
        PlanCode = plan.Code,
        PlanName = plan.Name,
        Cycle = cycle,
        Amount = payment.Amount,
        Currency = payment.Currency,
        IssuedAtUtc = nowUtc,
        PeriodStartUtc = payment.PeriodStartUtc,
        PeriodEndUtc = payment.PeriodEndUtc,
        Status = InvoiceStatus.Paid
    };

    public Result Void(string reason, Guid adminId, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            return Result.Failure(SubscriptionErrors.ReasonRequired);
        }

        if (Status == InvoiceStatus.Void)
        {
            return Result.Failure(SubscriptionErrors.InvalidTransition);
        }

        Status = InvoiceStatus.Void;
        VoidReason = reason.Trim();
        VoidedAtUtc = nowUtc;
        VoidedBy = adminId;
        return Result.Success();
    }
}

public static class SubscriptionErrors
{
    public static readonly Error NotFound = new("Subscription.NotFound", "Subscription not found.");
    public static readonly Error AlreadySubscribed = new("Subscription.AlreadySubscribed", "This owner already has a subscription. Change its plan instead.");
    public static readonly Error ReasonRequired = new("Subscription.ReasonRequired", "A reason is required.");
    public static readonly Error InvalidTransition = new("Subscription.InvalidTransition", "This change is not possible in the subscription's current state.");
    public static readonly Error Closed = new("Subscription.Closed", "The subscription is cancelled. Start a new one instead.");
    public static readonly Error InvalidPayment = new("Subscription.InvalidPayment", "Enter an amount above zero, a valid method and a reference of up to 100 characters.");
    public static readonly Error PaymentProofRequired = new("Subscription.PaymentProofRequired", "A payment proof must be submitted before the subscription request can be approved.");
    public static readonly Error DowngradeNotPossible = new("Subscription.DowngradeNotPossible", "The owner uses more than the new plan allows. Reduce usage first.");
    public static readonly Error InvoiceNotFound = new("Invoice.NotFound", "Invoice not found.");
}
