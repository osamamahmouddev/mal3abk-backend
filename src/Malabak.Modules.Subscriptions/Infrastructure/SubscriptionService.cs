using System.Globalization;
using Malabak.Modules.Subscriptions.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Subscriptions.Infrastructure;

public sealed record SubscriptionDto(
    Guid Id,
    Guid OwnerId,
    PlanDto Plan,
    string Cycle,
    string Status,
    DateTime StartedAtUtc,
    DateTime CurrentPeriodEndUtc,
    DateTime? GraceEndsAtUtc,
    string? StatusReason,
    bool AllowsNewResources);

public sealed record SubscriptionEventDto(string Type, Guid? FromPlanId, Guid? ToPlanId, string? Note, Guid? ActorId, DateTime OccurredAtUtc);

public sealed record SubscriptionDetailDto(SubscriptionDto Subscription, IReadOnlyList<SubscriptionEventDto> History, IReadOnlyList<PaymentDto> Payments);

public sealed record PaymentDto(Guid Id, decimal Amount, string Currency, string Method, string? Reference, DateTime PaidAtUtc, DateTime PeriodStartUtc, DateTime PeriodEndUtc, Guid RecordedBy, string? InvoiceNumber);

/// <summary>What a venue owner sees on the billing page: plan, usage against limits and recent invoices.</summary>
public sealed record OwnerBillingDto(SubscriptionDto? Subscription, PlanDto? EffectivePlan, OwnerUsage Usage, IReadOnlyList<InvoiceDto> Invoices);

public sealed record RecordPaymentInput(decimal Amount, PaymentMethod Method, string? Reference, DateTime? PaidAtUtc, string? Notes);

public sealed record SubscriptionListItem(Guid Id, Guid OwnerId, string PlanCode, string PlanName, string Cycle, string Status, DateTime CurrentPeriodEndUtc, DateTime? GraceEndsAtUtc);

/// <summary>
/// Subscription lifecycle for admins (TASK-708): assign, change plan (downgrades must fit current usage), record manual
/// payments (each issues an invoice), suspend, reactivate, cancel. Every action is audited and kept in the history.
/// </summary>
public sealed class SubscriptionService
{
    private readonly SubscriptionsDbContext _dbContext;
    private readonly IOwnerUsageReader _usage;
    private readonly IInvoiceNumberGenerator _numbers;
    private readonly INotificationPublisher _notifications;
    private readonly IAuditLog _audit;
    private readonly IClock _clock;

    public SubscriptionService(
        SubscriptionsDbContext dbContext, IOwnerUsageReader usage, IInvoiceNumberGenerator numbers, INotificationPublisher notifications, IAuditLog audit, IClock clock)
    {
        _dbContext = dbContext;
        _usage = usage;
        _numbers = numbers;
        _notifications = notifications;
        _audit = audit;
        _clock = clock;
    }

    public async Task<IReadOnlyList<SubscriptionListItem>> ListAsync(SubscriptionStatus? status, Guid? planId, int? expiringWithinDays, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.Subscriptions.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(x => x.Status == s);
        }

        if (planId is { } p)
        {
            query = query.Where(x => x.PlanId == p);
        }

        if (expiringWithinDays is { } days)
        {
            var until = _clock.UtcNow.AddDays(Math.Clamp(days, 0, 365));
            query = query.Where(x => (x.Status == SubscriptionStatus.Trial || x.Status == SubscriptionStatus.Active) && x.CurrentPeriodEndUtc <= until);
        }

        var rows = await query.OrderBy(x => x.CurrentPeriodEndUtc).Take(500).ToListAsync(cancellationToken);
        var plans = await _dbContext.Plans.AsNoTracking().ToDictionaryAsync(x => x.Id, cancellationToken);
        return rows.Select(x => new SubscriptionListItem(x.Id, x.OwnerId, plans.GetValueOrDefault(x.PlanId)?.Code ?? "", plans.GetValueOrDefault(x.PlanId)?.Name ?? "",
            x.Cycle.ToString(), x.Status.ToString(), x.CurrentPeriodEndUtc, x.GraceEndsAtUtc)).ToList();
    }

    public async Task<Result<SubscriptionDetailDto>> GetByOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.Subscriptions.AsNoTracking().Include(s => s.Events).FirstOrDefaultAsync(s => s.OwnerId == ownerId, cancellationToken);
        if (subscription is null)
        {
            return Result.Failure<SubscriptionDetailDto>(SubscriptionErrors.NotFound);
        }

        var payments = await _dbContext.Payments.AsNoTracking().Where(p => p.SubscriptionId == subscription.Id).OrderByDescending(p => p.PaidAtUtc).ToListAsync(cancellationToken);
        var invoiceNumbers = await _dbContext.Invoices.AsNoTracking().Where(i => i.SubscriptionId == subscription.Id).ToDictionaryAsync(i => i.PaymentId, i => i.Number, cancellationToken);
        return Result.Success(new SubscriptionDetailDto(
            await ToDtoAsync(subscription, cancellationToken),
            subscription.Events.OrderByDescending(e => e.OccurredAtUtc).Select(e => new SubscriptionEventDto(e.Type.ToString(), e.FromPlanId, e.ToPlanId, e.Note, e.ActorId, e.OccurredAtUtc)).ToList(),
            payments.Select(p => new PaymentDto(p.Id, p.Amount, p.Currency, p.Method.ToString(), p.Reference, p.PaidAtUtc, p.PeriodStartUtc, p.PeriodEndUtc, p.RecordedBy,
                invoiceNumbers.GetValueOrDefault(p.Id))).ToList()));
    }

    public async Task<OwnerBillingDto> GetOwnerBillingAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.OwnerId == ownerId, cancellationToken);
        var plan = subscription is not null
            ? await _dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == subscription.PlanId, cancellationToken)
            : await _dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.IsDefault && p.Status == PlanStatus.Active, cancellationToken);
        var invoices = await _dbContext.Invoices.AsNoTracking().Where(i => i.OwnerId == ownerId).OrderByDescending(i => i.IssuedAtUtc).Take(50).ToListAsync(cancellationToken);
        return new OwnerBillingDto(
            subscription is null ? null : await ToDtoAsync(subscription, cancellationToken),
            plan is null ? null : PlanService.ToDto(plan, 0),
            await _usage.GetAsync(ownerId, cancellationToken),
            invoices.Select(InvoiceService.ToDto).ToList());
    }

    public async Task<Result<SubscriptionDto>> AssignAsync(Guid ownerId, Guid planId, BillingCycle cycle, bool withTrial, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (await _dbContext.Subscriptions.AnyAsync(s => s.OwnerId == ownerId, cancellationToken))
        {
            return Result.Failure<SubscriptionDto>(SubscriptionErrors.AlreadySubscribed);
        }

        var plan = await AssignablePlanAsync(planId, cancellationToken);
        if (plan.IsFailure)
        {
            return Result.Failure<SubscriptionDto>(plan.Error);
        }

        var subscription = OwnerSubscription.Start(ownerId, plan.Value, cycle, withTrial, adminId, _clock.UtcNow);
        _dbContext.Subscriptions.Add(subscription);
        await _dbContext.SaveChangesAsync(cancellationToken);
        var dto = await ToDtoAsync(subscription, cancellationToken);
        await _audit.RecordAsync(new AuditEntry("subscription.assigned", "Subscription", subscription.Id.ToString("D"), adminId, AuditActorType.Admin, After: dto), cancellationToken);
        return Result.Success(dto);
    }

    public async Task<Result<SubscriptionDto>> ChangePlanAsync(Guid ownerId, Guid planId, BillingCycle cycle, string? note, Guid adminId, CancellationToken cancellationToken = default)
    {
        var subscription = await _dbContext.Subscriptions.Include(s => s.Events).FirstOrDefaultAsync(s => s.OwnerId == ownerId, cancellationToken);
        if (subscription is null)
        {
            return Result.Failure<SubscriptionDto>(SubscriptionErrors.NotFound);
        }

        var plan = await AssignablePlanAsync(planId, cancellationToken);
        if (plan.IsFailure)
        {
            return Result.Failure<SubscriptionDto>(plan.Error);
        }

        // A downgrade cannot leave the owner above the new limits (nothing is deleted behind their back).
        var usage = await _usage.GetAsync(ownerId, cancellationToken);
        if ((plan.Value.MaxVenues is { } v && usage.Venues > v) || (plan.Value.MaxPitches is { } p && usage.Pitches > p)
            || (plan.Value.MaxStaffPerVenue is { } st && usage.MaxStaffInOneVenue > st))
        {
            return Result.Failure<SubscriptionDto>(SubscriptionErrors.DowngradeNotPossible);
        }

        var before = await ToDtoAsync(subscription, cancellationToken);
        var changed = subscription.ChangePlan(plan.Value, cycle, adminId, note, _clock.UtcNow);
        if (changed.IsFailure)
        {
            return Result.Failure<SubscriptionDto>(changed.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        var after = await ToDtoAsync(subscription, cancellationToken);
        await _audit.RecordAsync(new AuditEntry("subscription.plan_changed", "Subscription", subscription.Id.ToString("D"), adminId, AuditActorType.Admin, before, after, note), cancellationToken);
        return Result.Success(after);
    }

    public async Task<Result<InvoiceDto>> RecordPaymentAsync(Guid ownerId, RecordPaymentInput input, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (input.Amount <= 0 || input.Amount > 10_000_000 || !Enum.IsDefined(input.Method) || input.Reference?.Length > 100 || input.Notes?.Length > 500)
        {
            return Result.Failure<InvoiceDto>(SubscriptionErrors.InvalidPayment);
        }

        var subscription = await _dbContext.Subscriptions.Include(s => s.Events).FirstOrDefaultAsync(s => s.OwnerId == ownerId, cancellationToken);
        if (subscription is null)
        {
            return Result.Failure<InvoiceDto>(SubscriptionErrors.NotFound);
        }

        if (subscription.Status is SubscriptionStatus.Cancelled or SubscriptionStatus.Suspended)
        {
            return Result.Failure<InvoiceDto>(SubscriptionErrors.InvalidTransition);
        }

        var plan = await _dbContext.Plans.FirstAsync(p => p.Id == subscription.PlanId, cancellationToken);
        var now = _clock.UtcNow;
        var paidAt = input.PaidAtUtc is { } at && at <= now ? at : now;
        var (start, end) = subscription.RecordPayment(adminId, input.Reference, now);
        var payment = SubscriptionPayment.Create(subscription.Id, ownerId, input.Amount, plan.Currency, input.Method, input.Reference, paidAt, start, end, adminId, now, input.Notes);
        var invoice = Invoice.Issue(await _numbers.NextAsync(now.Year, cancellationToken), payment, plan, subscription.Cycle, now);
        _dbContext.Payments.Add(payment);
        _dbContext.Invoices.Add(invoice);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(new AuditEntry("subscription.payment_recorded", "Subscription", subscription.Id.ToString("D"), adminId, AuditActorType.Admin,
            After: new { payment.Amount, payment.Currency, Method = payment.Method.ToString(), payment.Reference, invoice.Number, PeriodEnd = end }), cancellationToken);
        await _notifications.PublishAsync(new NotificationRequest(ownerId, NotificationTypes.SubscriptionPaymentRecorded,
            new Dictionary<string, string>
            {
                ["amount"] = string.Create(CultureInfo.InvariantCulture, $"{payment.Amount:0.##} {payment.Currency}"),
                ["planName"] = plan.Name,
                ["endDate"] = end.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["invoiceNumber"] = invoice.Number,
            },
            $"subscription-payment:{payment.Id:N}"), cancellationToken);
        return Result.Success(InvoiceService.ToDto(invoice));
    }

    public Task<Result<SubscriptionDto>> SuspendAsync(Guid ownerId, string reason, Guid adminId, CancellationToken cancellationToken = default) =>
        TransitionAsync(ownerId, "subscription.suspended", reason, adminId, (s, now) => s.Suspend(reason, adminId, now), notifySuspended: true, cancellationToken);

    public Task<Result<SubscriptionDto>> ReactivateAsync(Guid ownerId, string? reason, Guid adminId, CancellationToken cancellationToken = default) =>
        TransitionAsync(ownerId, "subscription.reactivated", reason, adminId, (s, now) => s.Reactivate(adminId, now), notifySuspended: false, cancellationToken);

    public Task<Result<SubscriptionDto>> CancelAsync(Guid ownerId, string reason, Guid adminId, CancellationToken cancellationToken = default) =>
        TransitionAsync(ownerId, "subscription.cancelled", reason, adminId, (s, now) => s.Cancel(reason, adminId, now), notifySuspended: false, cancellationToken);

    private async Task<Result<SubscriptionDto>> TransitionAsync(
        Guid ownerId, string action, string? reason, Guid adminId, Func<OwnerSubscription, DateTime, Result> change, bool notifySuspended, CancellationToken cancellationToken)
    {
        var subscription = await _dbContext.Subscriptions.Include(s => s.Events).FirstOrDefaultAsync(s => s.OwnerId == ownerId, cancellationToken);
        if (subscription is null)
        {
            return Result.Failure<SubscriptionDto>(SubscriptionErrors.NotFound);
        }

        var before = subscription.Status.ToString();
        var changed = change(subscription, _clock.UtcNow);
        if (changed.IsFailure)
        {
            return Result.Failure<SubscriptionDto>(changed.Error);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await _audit.RecordAsync(new AuditEntry(action, "Subscription", subscription.Id.ToString("D"), adminId, AuditActorType.Admin,
            new { Status = before }, new { Status = subscription.Status.ToString() }, reason), cancellationToken);
        if (notifySuspended)
        {
            await _notifications.PublishAsync(new NotificationRequest(ownerId, NotificationTypes.SubscriptionSuspended,
                new Dictionary<string, string> { ["reason"] = reason ?? "" }, $"subscription-suspended:{subscription.Id:N}:{_clock.UtcNow:yyyyMMddHHmm}"), cancellationToken);
        }

        return Result.Success(await ToDtoAsync(subscription, cancellationToken));
    }

    private async Task<Result<SubscriptionPlan>> AssignablePlanAsync(Guid planId, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.Plans.FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<SubscriptionPlan>(PlanErrors.NotFound);
        }

        return plan.Status == PlanStatus.Active ? Result.Success(plan) : Result.Failure<SubscriptionPlan>(PlanErrors.NotAssignable);
    }

    private async Task<SubscriptionDto> ToDtoAsync(OwnerSubscription s, CancellationToken cancellationToken)
    {
        var plan = await _dbContext.Plans.AsNoTracking().FirstAsync(p => p.Id == s.PlanId, cancellationToken);
        return new SubscriptionDto(s.Id, s.OwnerId, PlanService.ToDto(plan, 0), s.Cycle.ToString(), s.Status.ToString(), s.StartedAtUtc, s.CurrentPeriodEndUtc,
            s.GraceEndsAtUtc, s.StatusReason, s.AllowsNewResources);
    }
}

/// <summary>Invoice numbers: INV-{year}-{sequence:000000}, never reused.</summary>
public interface IInvoiceNumberGenerator
{
    Task<string> NextAsync(int year, CancellationToken cancellationToken = default);
}

/// <summary>Uses a PostgreSQL sequence so concurrent payments never get the same number (falls back to a count for in-memory tests).</summary>
public sealed class InvoiceNumberGenerator : IInvoiceNumberGenerator
{
    public const string Sequence = "invoice_number_seq";

    private readonly SubscriptionsDbContext _dbContext;

    public InvoiceNumberGenerator(SubscriptionsDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<string> NextAsync(int year, CancellationToken cancellationToken = default)
    {
        long next;
        if (_dbContext.Database.IsRelational())
        {
            next = await _dbContext.Database
                .SqlQuery<long>($"SELECT nextval('subscriptions.invoice_number_seq') AS \"Value\"")
                .SingleAsync(cancellationToken);
        }
        else
        {
            next = await _dbContext.Invoices.CountAsync(cancellationToken) + 1;
        }

        return string.Create(CultureInfo.InvariantCulture, $"INV-{year}-{next:000000}");
    }
}
