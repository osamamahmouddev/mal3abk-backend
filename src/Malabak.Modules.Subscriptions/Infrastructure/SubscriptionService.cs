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

public sealed record OwnerSubscriptionRequestDto(
    Guid Id,
    Guid PlanId,
    string PlanName,
    string Cycle,
    decimal Amount,
    string Status,
    string? Notes,
    string? AdminReply,
    DateTime CreatedAtUtc,
    bool IsPaymentReported = false,
    string? PaymentProofUrl = null,
    string? TransferReference = null,
    string? TransferNotes = null,
    DateTime? PaymentTransferredAtUtc = null,
    Guid? PaymentProofMediaId = null);

public sealed record SubscriptionRequestDto(
    Guid Id,
    Guid OwnerId,
    string? OwnerName,
    string? OwnerPhone,
    string? OwnerEmail,
    string? CurrentPlanName,
    PlanDto? RequestedPlan,
    string Cycle,
    decimal Amount,
    string Status,
    string? Notes,
    string? AdminReply,
    DateTime CreatedAtUtc,
    DateTime? ReviewedAtUtc,
    bool IsPaymentReported = false,
    string? PaymentProofUrl = null,
    string? TransferReference = null,
    string? TransferNotes = null,
    DateTime? PaymentTransferredAtUtc = null,
    Guid? PaymentProofMediaId = null);

/// <summary>What a venue owner sees on the billing page: plan, usage against limits, recent invoices and pending request.</summary>
public sealed record OwnerBillingDto(SubscriptionDto? Subscription, PlanDto? EffectivePlan, OwnerUsage Usage, IReadOnlyList<InvoiceDto> Invoices, OwnerSubscriptionRequestDto? PendingRequest = null);

public sealed record RecordPaymentInput(decimal Amount, PaymentMethod Method, string? Reference, DateTime? PaidAtUtc, string? Notes);

public sealed record SubscriptionVenueInfo(Guid Id, string Name, string City);

public sealed record SubscriptionListItem(
    Guid Id,
    Guid OwnerId,
    string? OwnerName,
    string? OwnerPhone,
    string? OwnerEmail,
    IReadOnlyList<SubscriptionVenueInfo> Venues,
    string PlanCode,
    string PlanName,
    string Cycle,
    string Status,
    DateTime StartedAtUtc,
    DateTime CurrentPeriodEndUtc,
    DateTime? GraceEndsAtUtc);

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
    private readonly IUserAccountsContract? _accounts;

    public SubscriptionService(
        SubscriptionsDbContext dbContext,
        IOwnerUsageReader usage,
        IInvoiceNumberGenerator numbers,
        INotificationPublisher notifications,
        IAuditLog audit,
        IClock clock,
        IUserAccountsContract? accounts = null)
    {
        _dbContext = dbContext;
        _usage = usage;
        _numbers = numbers;
        _notifications = notifications;
        _audit = audit;
        _clock = clock;
        _accounts = accounts;
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
        var ownerIds = rows.Select(x => x.OwnerId).Distinct().ToList();

        IReadOnlyDictionary<Guid, UserAccountSummary> summaries = new Dictionary<Guid, UserAccountSummary>();
        if (_accounts is not null && ownerIds.Count > 0)
        {
            summaries = await _accounts.GetSummariesAsync(ownerIds, cancellationToken);
        }

        var venuesMap = await _usage.GetOwnerVenuesBatchAsync(ownerIds, cancellationToken);

        return rows.Select(x =>
        {
            var plan = plans.GetValueOrDefault(x.PlanId);
            var owner = summaries.GetValueOrDefault(x.OwnerId);
            var ownerVenues = venuesMap.GetValueOrDefault(x.OwnerId) ?? [];
            var venueDtos = ownerVenues.Select(v => new SubscriptionVenueInfo(v.Id, v.Name, v.City)).ToList();

            return new SubscriptionListItem(
                x.Id,
                x.OwnerId,
                owner?.FullName,
                owner?.MaskedPhone,
                owner?.Email,
                venueDtos,
                plan?.Code ?? "",
                plan?.Name ?? "",
                x.Cycle.ToString(),
                x.Status.ToString(),
                x.StartedAtUtc,
                x.CurrentPeriodEndUtc,
                x.GraceEndsAtUtc);
        }).ToList();
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
        var pendingReq = await GetLatestRequestForOwnerAsync(ownerId, cancellationToken);
        return new OwnerBillingDto(
            subscription is null ? null : await ToDtoAsync(subscription, cancellationToken),
            plan is null ? null : PlanService.ToDto(plan, 0),
            await _usage.GetAsync(ownerId, cancellationToken),
            invoices.Select(i => InvoiceService.ToDto(i)).ToList(),
            pendingReq);
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

    public async Task<Result<SubscriptionDto>> AssignOrChangeAsync(
        Guid ownerId, Guid planId, BillingCycle cycle, bool withTrial, Guid adminId, CancellationToken cancellationToken = default)
    {
        var existing = await _dbContext.Subscriptions.AsNoTracking().FirstOrDefaultAsync(s => s.OwnerId == ownerId, cancellationToken);
        if (existing is not null)
        {
            return await ChangePlanAsync(ownerId, planId, cycle, "إسناد وتحديث مباشر من الإدارة", adminId, cancellationToken);
        }

        return await AssignAsync(ownerId, planId, cycle, withTrial, adminId, cancellationToken);
    }

    public async Task<Result<OwnerSubscriptionRequestDto>> CreateOrUpdateChangeRequestAsync(
        Guid ownerId, Guid planId, BillingCycle cycle, string? notes, CancellationToken cancellationToken = default)
    {
        var plan = await _dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId, cancellationToken);
        if (plan is null || plan.Status != PlanStatus.Active)
        {
            return Result.Failure<OwnerSubscriptionRequestDto>(SubscriptionErrors.NotFound);
        }

        var usage = await _usage.GetAsync(ownerId, cancellationToken);
        if ((plan.MaxVenues is { } v && usage.Venues > v) || (plan.MaxPitches is { } p && usage.Pitches > p)
            || (plan.MaxStaffPerVenue is { } st && usage.MaxStaffInOneVenue > st))
        {
            return Result.Failure<OwnerSubscriptionRequestDto>(SubscriptionErrors.DowngradeNotPossible);
        }

        var now = _clock.UtcNow;
        var existingPending = await _dbContext.SubscriptionRequests
            .FirstOrDefaultAsync(r => r.OwnerId == ownerId && r.Status == SubscriptionRequestStatus.Pending, cancellationToken);

        if (existingPending is not null)
        {
            existingPending.UpdatePending(planId, cycle, notes, now);
        }
        else
        {
            var req = SubscriptionChangeRequest.Create(ownerId, planId, cycle, notes, now);
            _dbContext.SubscriptionRequests.Add(req);
            existingPending = req;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        var amount = cycle == BillingCycle.Annual ? plan.AnnualPrice : plan.MonthlyPrice;
        return Result.Success(new OwnerSubscriptionRequestDto(
            existingPending.Id,
            plan.Id,
            plan.Name,
            cycle.ToString(),
            amount,
            existingPending.Status.ToString(),
            existingPending.Notes,
            existingPending.AdminReply,
            existingPending.CreatedAtUtc,
            existingPending.IsPaymentReported,
            existingPending.PaymentProofUrl,
            existingPending.TransferReference,
            existingPending.TransferNotes,
            existingPending.PaymentTransferredAtUtc,
            existingPending.PaymentProofMediaId));
    }

    public async Task<OwnerSubscriptionRequestDto?> GetLatestRequestForOwnerAsync(Guid ownerId, CancellationToken cancellationToken = default)
    {
        var req = await _dbContext.SubscriptionRequests.AsNoTracking()
            .Where(r => r.OwnerId == ownerId)
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (req is null)
        {
            return null;
        }

        var plan = await _dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.PlanId, cancellationToken);
        var planName = plan?.Name ?? "باقة";
        decimal amount = 0m;
        if (plan is not null)
        {
            amount = req.Cycle == BillingCycle.Annual ? plan.AnnualPrice : plan.MonthlyPrice;
        }

        return new OwnerSubscriptionRequestDto(
            req.Id,
            req.PlanId,
            planName,
            req.Cycle.ToString(),
            amount,
            req.Status.ToString(),
            req.Notes,
            req.AdminReply,
            req.CreatedAtUtc,
            req.IsPaymentReported,
            req.PaymentProofUrl,
            req.TransferReference,
            req.TransferNotes,
            req.PaymentTransferredAtUtc,
            req.PaymentProofMediaId);
    }

    public async Task<IReadOnlyList<SubscriptionRequestDto>> ListRequestsAsync(SubscriptionRequestStatus? status, CancellationToken cancellationToken = default)
    {
        var query = _dbContext.SubscriptionRequests.AsNoTracking();
        if (status is { } s)
        {
            query = query.Where(r => r.Status == s);
        }

        var rows = await query.OrderByDescending(r => r.CreatedAtUtc).Take(200).ToListAsync(cancellationToken);
        if (rows.Count == 0)
        {
            return [];
        }

        var ownerIds = rows.Select(r => r.OwnerId).Distinct().ToList();
        var planIds = rows.Select(r => r.PlanId).Distinct().ToList();

        var plans = await _dbContext.Plans.AsNoTracking().Where(p => planIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);
        var currentSubs = await _dbContext.Subscriptions.AsNoTracking().Where(s => ownerIds.Contains(s.OwnerId)).ToDictionaryAsync(s => s.OwnerId, cancellationToken);
        var currentSubPlanIds = currentSubs.Values.Select(s => s.PlanId).Distinct().ToList();
        var currentPlans = await _dbContext.Plans.AsNoTracking().Where(p => currentSubPlanIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, cancellationToken);

        IReadOnlyDictionary<Guid, UserAccountSummary> summaries = new Dictionary<Guid, UserAccountSummary>();
        if (_accounts is not null)
        {
            summaries = await _accounts.GetSummariesAsync(ownerIds, cancellationToken);
        }

        return rows.Select(r =>
        {
            var plan = plans.GetValueOrDefault(r.PlanId);
            var planDto = plan is not null ? PlanService.ToDto(plan, 0) : null;
            var currentSub = currentSubs.GetValueOrDefault(r.OwnerId);
            var currentPlan = currentSub is not null ? currentPlans.GetValueOrDefault(currentSub.PlanId) : null;
            var owner = summaries.GetValueOrDefault(r.OwnerId);
            var amount = r.Cycle == BillingCycle.Annual ? (plan?.AnnualPrice ?? 0) : (plan?.MonthlyPrice ?? 0);
            var currentPlanName = currentPlan?.Name ?? (currentSub is null ? "من غير باقة" : "افتراضية");

            return new SubscriptionRequestDto(
                r.Id,
                r.OwnerId,
                owner?.FullName,
                owner?.MaskedPhone,
                owner?.Email,
                currentPlanName,
                planDto,
                r.Cycle.ToString(),
                amount,
                r.Status.ToString(),
                r.Notes,
                r.AdminReply,
                r.CreatedAtUtc,
                r.ReviewedAtUtc,
                r.IsPaymentReported,
                r.PaymentProofUrl,
                r.TransferReference,
                r.TransferNotes,
                r.PaymentTransferredAtUtc,
                r.PaymentProofMediaId);
        }).ToList();
    }

    public async Task<Result<OwnerSubscriptionRequestDto>> ReportPaymentAsync(
        Guid ownerId,
        Guid? paymentProofMediaId,
        string? paymentProofUrl,
        string? transferReference,
        string? transferNotes,
        CancellationToken cancellationToken = default)
    {
        var req = await _dbContext.SubscriptionRequests
            .OrderByDescending(r => r.CreatedAtUtc)
            .FirstOrDefaultAsync(r => r.OwnerId == ownerId && r.Status == SubscriptionRequestStatus.Pending, cancellationToken);

        if (req is null)
        {
            return Result.Failure<OwnerSubscriptionRequestDto>(SubscriptionErrors.NotFound);
        }

        var now = _clock.UtcNow;
        req.ReportPayment(paymentProofMediaId, paymentProofUrl, transferReference, transferNotes, now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var plan = await _dbContext.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.PlanId, cancellationToken);
        var planName = plan?.Name ?? "الخطة المطلوبة";
        var amount = req.Cycle == BillingCycle.Annual ? (plan?.AnnualPrice ?? 0) : (plan?.MonthlyPrice ?? 0);

        await _notifications.PublishAsync(new NotificationRequest(
            ownerId,
            NotificationTypes.SubscriptionPaymentReported,
            new Dictionary<string, string>
            {
                ["planName"] = planName,
                ["amount"] = amount.ToString("F2", CultureInfo.InvariantCulture)
            },
            $"subscription-payment-reported:{req.Id:N}:{now.Ticks}"), cancellationToken);

        await _audit.RecordAsync(new AuditEntry(
            "subscription_request.payment_reported",
            "SubscriptionRequest",
            req.Id.ToString("D"),
            ownerId,
            AuditActorType.User,
            After: new
            {
                req.OwnerId,
                req.PlanId,
                PaymentProofMediaId = paymentProofMediaId,
                TransferReference = transferReference,
                TransferNotes = transferNotes
            }), cancellationToken);

        return Result.Success(new OwnerSubscriptionRequestDto(
            req.Id,
            req.PlanId,
            planName,
            req.Cycle.ToString(),
            amount,
            req.Status.ToString(),
            req.Notes,
            req.AdminReply,
            req.CreatedAtUtc,
            req.IsPaymentReported,
            req.PaymentProofUrl,
            req.TransferReference,
            req.TransferNotes,
            req.PaymentTransferredAtUtc,
            req.PaymentProofMediaId));
    }

    public async Task<Result<SubscriptionDto>> ApproveRequestAsync(Guid requestId, Guid adminId, CancellationToken cancellationToken = default)
    {
        var req = await _dbContext.SubscriptionRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (req is null)
        {
            return Result.Failure<SubscriptionDto>(SubscriptionErrors.NotFound);
        }

        if (req.Status != SubscriptionRequestStatus.Pending)
        {
            return Result.Failure<SubscriptionDto>(SubscriptionErrors.InvalidTransition);
        }

        if (!req.IsPaymentReported)
        {
            return Result.Failure<SubscriptionDto>(SubscriptionErrors.PaymentProofRequired);
        }

        var plan = await _dbContext.Plans.FirstOrDefaultAsync(p => p.Id == req.PlanId, cancellationToken);
        if (plan is null)
        {
            return Result.Failure<SubscriptionDto>(SubscriptionErrors.NotFound);
        }

        var existing = await _dbContext.Subscriptions.Include(s => s.Events).FirstOrDefaultAsync(s => s.OwnerId == req.OwnerId, cancellationToken);
        Result<SubscriptionDto> subResult;
        if (existing is not null)
        {
            subResult = await ChangePlanAsync(req.OwnerId, req.PlanId, req.Cycle, "تمت الترقية بناءً على طلب المالك", adminId, cancellationToken);
        }
        else
        {
            subResult = await AssignAsync(req.OwnerId, req.PlanId, req.Cycle, withTrial: false, adminId, cancellationToken);
        }

        if (subResult.IsFailure)
        {
            return subResult;
        }

        var now = _clock.UtcNow;
        req.Approve(adminId, now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _notifications.PublishAsync(new NotificationRequest(
            req.OwnerId,
            NotificationTypes.SubscriptionRequestApproved,
            new Dictionary<string, string>
            {
                ["planName"] = plan.Name
            },
            $"subscription-approved:{req.Id:N}"), cancellationToken);

        await _audit.RecordAsync(new AuditEntry(
            "subscription_request.approved",
            "SubscriptionRequest",
            req.Id.ToString("D"),
            adminId,
            AuditActorType.Admin,
            After: new { req.OwnerId, req.PlanId, PlanName = plan.Name, Cycle = req.Cycle.ToString() }), cancellationToken);

        return subResult;
    }

    public async Task<Result> ReplyToRequestAsync(Guid requestId, string instructions, Guid adminId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(instructions))
        {
            return Result.Failure(Error.Validation("SubscriptionRequest.ReplyRequired", "Payment instructions cannot be empty."));
        }

        var req = await _dbContext.SubscriptionRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (req is null)
        {
            return Result.Failure(SubscriptionErrors.NotFound);
        }

        var now = _clock.UtcNow;
        req.Reply(adminId, instructions, now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _notifications.PublishAsync(new NotificationRequest(
            req.OwnerId,
            NotificationTypes.SubscriptionRequestInstructions,
            new Dictionary<string, string>
            {
                ["instructions"] = instructions
            },
            $"subscription-instructions:{req.Id:N}:{now.Ticks}"), cancellationToken);

        await _audit.RecordAsync(new AuditEntry(
            "subscription_request.replied",
            "SubscriptionRequest",
            req.Id.ToString("D"),
            adminId,
            AuditActorType.Admin,
            After: new { req.OwnerId, Instructions = instructions }), cancellationToken);

        return Result.Success();
    }

    public async Task<Result> RejectRequestAsync(Guid requestId, string? reason, Guid adminId, CancellationToken cancellationToken = default)
    {
        var req = await _dbContext.SubscriptionRequests.FirstOrDefaultAsync(r => r.Id == requestId, cancellationToken);
        if (req is null)
        {
            return Result.Failure(SubscriptionErrors.NotFound);
        }

        var now = _clock.UtcNow;
        req.Reject(adminId, reason, now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _notifications.PublishAsync(new NotificationRequest(
            req.OwnerId,
            NotificationTypes.SubscriptionRequestRejected,
            new Dictionary<string, string>
            {
                ["reason"] = string.IsNullOrWhiteSpace(reason) ? "تم رفض الطلب من قِبل الإدارة." : reason
            },
            $"subscription-rejected:{req.Id:N}"), cancellationToken);

        await _audit.RecordAsync(new AuditEntry(
            "subscription_request.rejected",
            "SubscriptionRequest",
            req.Id.ToString("D"),
            adminId,
            AuditActorType.Admin,
            After: new { req.OwnerId, Reason = reason }), cancellationToken);

        return Result.Success();
    }

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
