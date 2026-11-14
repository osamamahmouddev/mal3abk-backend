using System.Security.Claims;
using Asp.Versioning.Builder;
using Malabak.Api.Infrastructure.Authorization;
using Malabak.Api.Infrastructure.Media;
using Malabak.Api.Infrastructure.Security;
using Malabak.Modules.Subscriptions.Domain;
using Malabak.Modules.Subscriptions.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Malabak.Api.Endpoints;

/// <summary>
/// SaaS plans, subscriptions and invoices (TASK-060, 707, 708, 709). Public plan list; admin management behind finance
/// permissions; venue owners see only their own billing and invoices.
/// </summary>
public static class SubscriptionEndpoints
{
    public static void MapSubscriptionEndpoints(this IEndpointRouteBuilder app, ApiVersionSet apiVersionSet)
    {
        var v1 = app.MapGroup("/api/v{version:apiVersion}").WithApiVersionSet(apiVersionSet).MapToApiVersion(1, 0);

        v1.MapGet("/plans", async (HttpContext http, PlanService plans, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "no-cache, must-revalidate";
            return Results.Ok(await plans.ListPublicAsync(ct));
        })
        .AllowAnonymous()
        .RequireDistributedRateLimiting("public-plans", permitLimit: 60, windowSeconds: 60)
        .Produces<IReadOnlyList<PlanDto>>()
        .WithTags("Plans")
        .WithName("ListPublicPlans")
        .WithSummary("Active public plans for venue owners");

        MapAdminPlans(v1.MapGroup("/admin/plans").RequireAuthorization(Permissions.SubscriptionsManage).WithTags("Admin Plans"));
        MapAdminSubscriptions(v1.MapGroup("/admin/subscriptions").RequireAuthorization(Permissions.SubscriptionsManage).WithTags("Admin Subscriptions"));
        MapAdminInvoices(v1.MapGroup("/admin/invoices").RequireAuthorization(Permissions.InvoicesView).WithTags("Admin Invoices"));

        var billing = v1.MapGroup("/venue-manager/billing").RequireAuthorization(Permissions.VenuesManage).WithTags("Venue Billing");
        billing.MapGet("", async (ClaimsPrincipal user, HttpContext http, SubscriptionService subscriptions, MediaService mediaService, CancellationToken ct) =>
        {
            http.Response.Headers.CacheControl = "private, no-store";
            var dto = await subscriptions.GetOwnerBillingAsync(user.GetUserId()!.Value, ct);
            if (dto.PendingRequest?.PaymentProofMediaId is { } mId)
            {
                var reqWithSigned = dto.PendingRequest with { PaymentProofUrl = mediaService.SignedUrl(mId, "large") };
                dto = dto with { PendingRequest = reqWithSigned };
            }
            return Results.Ok(dto);
        })
        .Produces<OwnerBillingDto>()
        .WithName("GetMyBilling")
        .WithSummary("The signed-in owner's plan, usage against limits and invoices");

        billing.MapGet("/invoices/{invoiceId:guid}/pdf", async (Guid invoiceId, ClaimsPrincipal user, InvoiceService invoices, CancellationToken ct) =>
            PdfResult(await invoices.PdfAsync(invoiceId, user.GetUserId()!.Value, ct), invoiceId))
        .RequireDistributedRateLimiting("invoice-pdf", permitLimit: 30, windowSeconds: 60)
        .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
        .ProducesProblem(StatusCodes.Status404NotFound)
        .WithName("DownloadMyInvoice")
        .WithSummary("Download one of the signed-in owner's invoices as PDF");

        billing.MapPost("/requests", async ([FromBody] RequestPlanChangeApiRequest request, ClaimsPrincipal user, SubscriptionService subscriptions, CancellationToken ct) =>
            ToResult(await subscriptions.CreateOrUpdateChangeRequestAsync(user.GetUserId()!.Value, request.PlanId, request.Cycle, request.Notes, ct)))
        .RequireDistributedRateLimiting("plan-change-request", permitLimit: 10, windowSeconds: 60)
        .Produces<OwnerSubscriptionRequestDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithName("RequestPlanChange")
        .WithSummary("Request an upgrade or change to a subscription plan");

        billing.MapGet("/requests/latest", async (ClaimsPrincipal user, SubscriptionService subscriptions, MediaService mediaService, CancellationToken ct) =>
        {
            var req = await subscriptions.GetLatestRequestForOwnerAsync(user.GetUserId()!.Value, ct);
            if (req?.PaymentProofMediaId is { } mId)
            {
                req = req with { PaymentProofUrl = mediaService.SignedUrl(mId, "large") };
            }
            return Results.Ok(req);
        })
        .Produces<OwnerSubscriptionRequestDto?>()
        .WithName("GetLatestPlanChangeRequest")
        .WithSummary("The latest plan change request submitted by the signed-in owner");

        billing.MapPost("/requests/report-payment", async (
            [FromBody] ReportSubscriptionPaymentApiRequest request,
            ClaimsPrincipal user,
            SubscriptionService subscriptions,
            MediaService mediaService,
            CancellationToken ct) =>
        {
            var userId = user.GetUserId()!.Value;
            string? proofUrl = null;
            if (request.PaymentProofMediaId is { } mediaId)
            {
                var asset = await mediaService.FindOwnedReadyAsync(mediaId, userId, MediaPurpose.PaymentProof, ct);
                if (asset is not null)
                {
                    proofUrl = mediaService.SignedUrl(mediaId, "large");
                }
            }

            return ToResult(await subscriptions.ReportPaymentAsync(
                userId,
                request.PaymentProofMediaId,
                proofUrl,
                request.TransferReference,
                request.TransferNotes,
                ct));
        })
        .RequireDistributedRateLimiting("plan-change-report-payment", permitLimit: 10, windowSeconds: 60)
        .Produces<OwnerSubscriptionRequestDto>()
        .ProducesProblem(StatusCodes.Status400BadRequest)
        .WithName("ReportSubscriptionPayment")
        .WithSummary("Venue owner reports payment and attaches receipt proof for their plan change request");
    }

    private static void MapAdminPlans(RouteGroupBuilder plans)
    {
        plans.MapGet("", async (PlanService service, CancellationToken ct) => Results.Ok(await service.ListAsync(ct)))
            .Produces<IReadOnlyList<PlanDto>>().WithName("AdminListPlans").WithSummary("All plans with subscriber counts");

        plans.MapPost("", async ([FromBody] PlanApiRequest request, ClaimsPrincipal user, PlanService service, CancellationToken ct) =>
            Created(await service.CreateAsync(request.ToTerms(), user.GetUserId()!.Value, ct), "/api/v1/admin/plans"))
            .Produces<PlanDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminCreatePlan").WithSummary("Create a plan (starts as draft)");

        plans.MapPut("/{planId:guid}", async (Guid planId, [FromBody] PlanApiRequest request, ClaimsPrincipal user, PlanService service, CancellationToken ct) =>
            ToResult(await service.UpdateAsync(planId, request.ToTerms(), user.GetUserId()!.Value, ct)))
            .Produces<PlanDto>().ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminUpdatePlan").WithSummary("Edit a plan's prices, limits and features");

        plans.MapPost("/{planId:guid}/status", async (Guid planId, [FromBody] PlanStatusApiRequest request, ClaimsPrincipal user, PlanService service, CancellationToken ct) =>
            ToResult(await service.SetStatusAsync(planId, request.Status, user.GetUserId()!.Value, ct)))
            .Produces<PlanDto>().ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminSetPlanStatus").WithSummary("Activate, archive or return a plan to draft");

        plans.MapPost("/{planId:guid}/default", async (Guid planId, ClaimsPrincipal user, PlanService service, CancellationToken ct) =>
            ToResult(await service.MakeDefaultAsync(planId, user.GetUserId()!.Value, ct)))
            .Produces<PlanDto>().ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminMakePlanDefault").WithSummary("Plan applied to owners without a subscription");
    }

    private static void MapAdminSubscriptions(RouteGroupBuilder subs)
    {
        subs.MapGet("", async ([FromQuery] SubscriptionStatus? status, [FromQuery] Guid? planId, [FromQuery] int? expiringWithinDays, SubscriptionService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(status, planId, expiringWithinDays, ct)))
            .Produces<IReadOnlyList<SubscriptionListItem>>().WithName("AdminListSubscriptions").WithSummary("Subscriptions by status, plan or upcoming expiry");

        subs.MapGet("/owners/{ownerId:guid}", async (Guid ownerId, SubscriptionService service, CancellationToken ct) => ToResult(await service.GetByOwnerAsync(ownerId, ct)))
            .Produces<SubscriptionDetailDto>().ProducesProblem(StatusCodes.Status404NotFound).WithName("AdminGetSubscription").WithSummary("An owner's subscription with history and payments");

        subs.MapPost("", async ([FromBody] AssignSubscriptionApiRequest request, ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            Created(await service.AssignAsync(request.OwnerId, request.PlanId, request.Cycle, request.WithTrial, user.GetUserId()!.Value, ct), "/api/v1/admin/subscriptions"))
            .Produces<SubscriptionDto>(StatusCodes.Status201Created).ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminAssignSubscription").WithSummary("Give an owner a plan (with optional trial)");

        subs.MapPut("/owners/{ownerId:guid}/plan", async (Guid ownerId, [FromBody] ChangePlanApiRequest request, ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            ToResult(await service.ChangePlanAsync(ownerId, request.PlanId, request.Cycle, request.Note, user.GetUserId()!.Value, ct)))
            .Produces<SubscriptionDto>().ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminChangePlan").WithSummary("Upgrade or downgrade (downgrades must fit current usage)");

        subs.MapPost("/owners/{ownerId:guid}/payments", async (Guid ownerId, [FromBody] RecordPaymentApiRequest request, ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            ToResult(await service.RecordPaymentAsync(ownerId, new RecordPaymentInput(request.Amount, request.Method, request.Reference, request.PaidAtUtc, request.Notes), user.GetUserId()!.Value, ct)))
            .RequireDistributedRateLimiting("admin-subscription-payment", permitLimit: 20, windowSeconds: 60)
            .Produces<InvoiceDto>().ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminRecordPayment").WithSummary("Record a manual payment: extends the period and issues an invoice");

        subs.MapPost("/owners/{ownerId:guid}/suspend", async (Guid ownerId, [FromBody] ReasonApiRequest request, ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            ToResult(await service.SuspendAsync(ownerId, request.Reason, user.GetUserId()!.Value, ct)))
            .Produces<SubscriptionDto>().ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminSuspendSubscription").WithSummary("Suspend (reason required; bookings keep working)");

        subs.MapPost("/owners/{ownerId:guid}/reactivate", async (Guid ownerId, [FromBody] ReasonApiRequest request, ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            ToResult(await service.ReactivateAsync(ownerId, request.Reason, user.GetUserId()!.Value, ct)))
            .Produces<SubscriptionDto>().ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminReactivateSubscription").WithSummary("Lift a suspension");

        subs.MapPost("/owners/{ownerId:guid}/cancel", async (Guid ownerId, [FromBody] ReasonApiRequest request, ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            ToResult(await service.CancelAsync(ownerId, request.Reason, user.GetUserId()!.Value, ct)))
            .Produces<SubscriptionDto>().ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminCancelSubscription").WithSummary("Cancel (reason required)");

        subs.MapGet("/requests", async ([FromQuery] SubscriptionRequestStatus? status, SubscriptionService service, MediaService mediaService, CancellationToken ct) =>
        {
            var list = await service.ListRequestsAsync(status, ct);
            var withSigned = list.Select(r => r.PaymentProofMediaId is { } mId
                ? r with { PaymentProofUrl = mediaService.SignedUrl(mId, "large") }
                : r).ToList();
            return Results.Ok(withSigned);
        })
        .Produces<IReadOnlyList<SubscriptionRequestDto>>().WithName("AdminListSubscriptionRequests").WithSummary("Pending or reviewed plan change requests");

        subs.MapPost("/requests/{requestId:guid}/approve", async (Guid requestId, ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            ToResult(await service.ApproveRequestAsync(requestId, user.GetUserId()!.Value, ct)))
            .Produces<SubscriptionDto>().ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminApproveSubscriptionRequest").WithSummary("Approve a plan change request and update the subscription");

        subs.MapPost("/requests/{requestId:guid}/reply", async (Guid requestId, [FromBody] ReplySubscriptionRequestApiRequest request, ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            ToResult(await service.ReplyToRequestAsync(requestId, request.Instructions, user.GetUserId()!.Value, ct)))
            .Produces(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminReplySubscriptionRequest").WithSummary("Send payment instructions for a plan request");

        subs.MapPost("/requests/{requestId:guid}/reject", async (Guid requestId, [FromBody] RejectSubscriptionRequestApiRequest request, ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            ToResult(await service.RejectRequestAsync(requestId, request.Reason, user.GetUserId()!.Value, ct)))
            .Produces(StatusCodes.Status200OK).ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminRejectSubscriptionRequest").WithSummary("Reject a plan change request");

        subs.MapPost("/assign-or-change", async ([FromBody] AssignSubscriptionApiRequest request, ClaimsPrincipal user, SubscriptionService service, CancellationToken ct) =>
            ToResult(await service.AssignOrChangeAsync(request.OwnerId, request.PlanId, request.Cycle, request.WithTrial, user.GetUserId()!.Value, ct)))
            .Produces<SubscriptionDto>().ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminAssignOrChangeSubscription").WithSummary("Assign or change plan for any account seamlessly");
    }

    private static void MapAdminInvoices(RouteGroupBuilder invoices)
    {
        invoices.MapGet("", async ([FromQuery] Guid? ownerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, InvoiceService service, CancellationToken ct) =>
            Results.Ok(await service.ListAsync(ownerId, from, to, ct)))
            .Produces<IReadOnlyList<InvoiceDto>>().WithName("AdminListInvoices").WithSummary("Invoices by owner and issue date");

        invoices.MapGet("/reconciliation", async ([FromQuery] DateOnly from, [FromQuery] DateOnly to, InvoiceService service, CancellationToken ct) =>
            to < from || to.DayNumber - from.DayNumber > 366
                ? Results.Problem(title: "Invoice.InvalidRange", detail: "Pick a range of up to a year.", statusCode: StatusCodes.Status400BadRequest)
                : Results.Ok(await service.ReconcileAsync(from, to, ct)))
            .Produces<ReconciliationDto>().ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminInvoiceReconciliation").WithSummary("Collected and voided totals by payment method");

        invoices.MapGet("/{invoiceId:guid}", async (Guid invoiceId, InvoiceService service, CancellationToken ct) => ToResult(await service.GetAsync(invoiceId, null, ct)))
            .Produces<InvoiceDto>().ProducesProblem(StatusCodes.Status404NotFound).WithName("AdminGetInvoice");

        invoices.MapGet("/{invoiceId:guid}/pdf", async (Guid invoiceId, InvoiceService service, CancellationToken ct) => PdfResult(await service.PdfAsync(invoiceId, null, ct), invoiceId))
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf").ProducesProblem(StatusCodes.Status404NotFound).WithName("AdminDownloadInvoice");

        invoices.MapPost("/{invoiceId:guid}/void", async (Guid invoiceId, [FromBody] ReasonApiRequest request, ClaimsPrincipal user, InvoiceService service, CancellationToken ct) =>
            ToResult(await service.VoidAsync(invoiceId, request.Reason, user.GetUserId()!.Value, ct)))
            .RequireAuthorization(Permissions.SubscriptionsManage)
            .Produces<InvoiceDto>().ProducesProblem(StatusCodes.Status400BadRequest).WithName("AdminVoidInvoice").WithSummary("Void an invoice (reason required; never deleted)");
    }

    private static IResult PdfResult(Result<byte[]> result, Guid invoiceId) =>
        result.IsSuccess ? Results.File(result.Value, "application/pdf", $"invoice-{invoiceId:N}.pdf") : ToResult(result);

    private static IResult Created<T>(Result<T> result, string location) =>
        result.IsSuccess ? Results.Created(location, result.Value) : ToResult(result);

    private static IResult ToResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok(result.Value);
        }

        var status = StatusCodes.Status400BadRequest;
        if (result.Error.Code.EndsWith("NotFound", StringComparison.Ordinal))
        {
            status = StatusCodes.Status404NotFound;
        }
        else if (result.Error.Code is "Plan.DuplicateCode" or "Subscription.AlreadySubscribed")
        {
            status = StatusCodes.Status409Conflict;
        }

        return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: status);
    }

    private static IResult ToResult(Result result)
    {
        if (result.IsSuccess)
        {
            return Results.Ok();
        }

        var status = StatusCodes.Status400BadRequest;
        if (result.Error.Code.EndsWith("NotFound", StringComparison.Ordinal))
        {
            status = StatusCodes.Status404NotFound;
        }

        return Results.Problem(title: result.Error.Code, detail: result.Error.Description, statusCode: status);
    }
}

public sealed record PlanApiRequest(
    string Code, string Name, string? Description, decimal MonthlyPrice, decimal AnnualPrice, int? MaxVenues, int? MaxPitches, int? MaxStaffPerVenue,
    IReadOnlyList<string>? Features, int TrialDays, int GraceDays, bool IsPublic, int SortOrder, string? Currency = null)
{
    public PlanTerms ToTerms() => new(Code, Name, Description, MonthlyPrice, AnnualPrice, MaxVenues, MaxPitches, MaxStaffPerVenue, Features ?? [], TrialDays, GraceDays,
        IsPublic, SortOrder, Currency ?? "EGP");
}

public sealed record PlanStatusApiRequest(PlanStatus Status);

public sealed record AssignSubscriptionApiRequest(Guid OwnerId, Guid PlanId, BillingCycle Cycle, bool WithTrial);

public sealed record ChangePlanApiRequest(Guid PlanId, BillingCycle Cycle, string? Note);

public sealed record RecordPaymentApiRequest(decimal Amount, PaymentMethod Method, string? Reference, DateTime? PaidAtUtc, string? Notes);

public sealed record ReasonApiRequest(string Reason);

public sealed record RequestPlanChangeApiRequest(Guid PlanId, BillingCycle Cycle, string? Notes);

public sealed record ReplySubscriptionRequestApiRequest(string Instructions);

public sealed record RejectSubscriptionRequestApiRequest(string? Reason);

public sealed record ReportSubscriptionPaymentApiRequest(Guid? PaymentProofMediaId, string? TransferReference, string? TransferNotes);
