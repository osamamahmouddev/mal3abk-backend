using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.Policies;

public sealed record BookingPolicyDto(
    Guid Id,
    int Version,
    DateTime EffectiveFromUtc,
    bool IsCurrent,
    BookingPolicySnapshot Policy);

/// <summary>Plain-language summary the player sees before booking (keys for localization on the client).</summary>
public sealed record BookingPolicyPreviewDto(
    int Version,
    string DepositSummaryKey,
    decimal DepositValue,
    int PayWithinMinutes,
    int ReviewWithinMinutes,
    int FreeCancellationUntilHoursBefore,
    string RefundSummaryKey,
    decimal RefundPercentage,
    string NoShowSummaryKey,
    int LateArrivalGraceMinutes,
    bool RescheduleAllowed,
    int RescheduleUntilHoursBefore,
    string? Terms);

public static class BookingPolicyErrors
{
    public static readonly Error NotConfigured = new("BookingPolicy.NotFound", "This venue has not configured a booking policy yet.");

    public static Error Invalid(IEnumerable<string> messages) => new("BookingPolicy.Invalid", string.Join(" ", messages));
}

public static class BookingPolicyMapping
{
    public static BookingPolicyDto ToDto(VenuePolicy p) => new(p.Id, p.Version, p.EffectiveFromUtc, p.IsCurrent, p.ToSnapshot());

    public static BookingPolicyPreviewDto ToPreview(BookingPolicySnapshot s) => new(
        s.Version,
        $"deposit.{s.DepositType}",
        s.DepositValue,
        s.ProofSubmissionWindowMinutes,
        s.ManualReviewWindowMinutes,
        s.CancellationDeadlineHours,
        $"refund.{s.RefundEligibility}",
        s.RefundPercentage,
        $"noShow.{s.NoShowRule}",
        s.LateArrivalGraceMinutes,
        s.AllowReschedule,
        s.RescheduleDeadlineHours,
        s.Terms);
}

public sealed record SetBookingPolicyCommand(Guid VenueId, Guid UserId, BookingPolicyTerms Terms) : ICommand<Result<BookingPolicyDto>>;

/// <summary>Creates a new policy version; existing bookings keep the snapshot of the version they were made under.</summary>
public sealed class SetBookingPolicyHandler : ICommandHandler<SetBookingPolicyCommand, Result<BookingPolicyDto>>
{
    private readonly IVenueAccessService _accessService;
    private readonly IVenueAuditLogger _auditLogger;
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;

    public SetBookingPolicyHandler(IVenueAccessService accessService, IVenueAuditLogger auditLogger, VenuesDbContext dbContext, IClock clock)
    {
        _accessService = accessService;
        _auditLogger = auditLogger;
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<BookingPolicyDto>> HandleAsync(SetBookingPolicyCommand command, CancellationToken cancellationToken = default)
    {
        var access = await _accessService.GetAccessAsync(command.UserId, command.VenueId, cancellationToken);
        if (!access.Can(Permissions.PoliciesManage))
        {
            return Result.Failure<BookingPolicyDto>(VenueAccessErrors.VenueNotFound);
        }

        var errors = command.Terms.Validate();
        if (errors.Count > 0)
        {
            return Result.Failure<BookingPolicyDto>(BookingPolicyErrors.Invalid(errors));
        }

        var current = await _dbContext.Policies.FirstOrDefaultAsync(p => p.VenueId == command.VenueId && p.IsCurrent, cancellationToken);

        // The deposit is paid to the platform (commission model), so only the platform team sets it: the venue's version
        // keeps the current deposit whatever the request carries.
        var terms = command.Terms with { Deposit = current?.ToDepositRule() ?? BookingPolicyTerms.DefaultDeposit };
        var next = VenuePolicy.CreateVersion(command.VenueId, current, terms, command.UserId, _clock.UtcNow);

        // Retire the old version in its own statement so the single-current index never sees two current rows.
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _dbContext.Policies.Add(next);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await _auditLogger.LogAsync("BookingPolicy.VersionCreated", nameof(VenuePolicy), next.Id, command.UserId,
            new { next.VenueId, next.Version, Before = current?.ToSnapshot(), After = next.ToSnapshot() }, cancellationToken);

        return Result.Success(BookingPolicyMapping.ToDto(next));
    }
}

public sealed record GetBookingPoliciesQuery(Guid VenueId, Guid UserId) : IQuery<Result<IReadOnlyList<BookingPolicyDto>>>;

/// <summary>Current version first, then history.</summary>
public sealed class GetBookingPoliciesHandler : IQueryHandler<GetBookingPoliciesQuery, Result<IReadOnlyList<BookingPolicyDto>>>
{
    private readonly IVenueAccessService _accessService;
    private readonly VenuesDbContext _dbContext;

    public GetBookingPoliciesHandler(IVenueAccessService accessService, VenuesDbContext dbContext)
    {
        _accessService = accessService;
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<BookingPolicyDto>>> HandleAsync(GetBookingPoliciesQuery query, CancellationToken cancellationToken = default)
    {
        var access = await _accessService.GetAccessAsync(query.UserId, query.VenueId, cancellationToken);
        if (!access.Can(Permissions.VenueView))
        {
            return Result.Failure<IReadOnlyList<BookingPolicyDto>>(VenueAccessErrors.VenueNotFound);
        }

        var versions = await _dbContext.Policies.AsNoTracking()
            .Where(p => p.VenueId == query.VenueId)
            .OrderByDescending(p => p.Version)
            .Take(50)
            .ToListAsync(cancellationToken);

        IReadOnlyList<BookingPolicyDto> dtos = versions.Select(BookingPolicyMapping.ToDto).ToList();
        return Result.Success(dtos);
    }
}

public sealed record GetBookingPolicyPreviewQuery(Guid VenueId) : IQuery<Result<BookingPolicyPreviewDto>>;

/// <summary>Customer-facing preview; only for venues players can actually see (approved and published).</summary>
public sealed class GetBookingPolicyPreviewHandler : IQueryHandler<GetBookingPolicyPreviewQuery, Result<BookingPolicyPreviewDto>>
{
    private readonly VenuesDbContext _dbContext;
    private readonly IBookingPolicyProvider _provider;

    public GetBookingPolicyPreviewHandler(VenuesDbContext dbContext, IBookingPolicyProvider provider)
    {
        _dbContext = dbContext;
        _provider = provider;
    }

    public async Task<Result<BookingPolicyPreviewDto>> HandleAsync(GetBookingPolicyPreviewQuery query, CancellationToken cancellationToken = default)
    {
        var visible = await _dbContext.Venues.AnyAsync(
            v => v.Id == query.VenueId && !v.IsDeleted && v.IsApproved && v.IsPublished, cancellationToken);
        if (!visible)
        {
            return Result.Failure<BookingPolicyPreviewDto>(VenueAccessErrors.VenueNotFound);
        }

        var snapshot = await _provider.GetCurrentSnapshotAsync(query.VenueId, cancellationToken);
        return snapshot is null
            ? Result.Failure<BookingPolicyPreviewDto>(BookingPolicyErrors.NotConfigured)
            : Result.Success(BookingPolicyMapping.ToPreview(snapshot));
    }
}
