using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Bookings.PlayerQueries;

public sealed record PlayerBookingSummaryDto(
    Guid Id,
    string Reference,
    Guid VenueId,
    Guid PitchId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal TotalPrice,
    decimal DepositAmount,
    string Currency,
    BookingStatus Status,
    DateTime? HoldExpiresAtUtc,
    PlayerAllowedActions AllowedActions,
    DateTime CreatedAtUtc);

public sealed record PlayerBookingDetailDto(
    Guid Id,
    string Reference,
    Guid VenueId,
    Guid PitchId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal TotalPrice,
    decimal DepositAmount,
    string Currency,
    BookingStatus Status,
    DateTime? HoldExpiresAtUtc,
    string? Notes,
    string? CancellationPolicySummary,
    PlayerVenueDto? Venue,
    PlayerAllowedActions AllowedActions,
    IReadOnlyList<PlayerProofDto> Proofs,
    IReadOnlyList<PlayerStatusHistoryDto> StatusHistory,
    DateTime CreatedAtUtc);

/// <summary>Where the booking is, for the confirmation screen and directions.</summary>
public sealed record PlayerVenueDto(string Name, string Address, double? Latitude, double? Longitude);

public sealed record PlayerAllowedActions(
    bool CanUploadProof,
    bool CanCancel,
    bool CanReview,
    string? CancellationDeadlineUtc);

public sealed record PlayerProofDto(
    Guid Id,
    string OriginalFileName,
    string ContentType,
    long FileSizeBytes,
    bool? IsApproved,
    string? RejectionReason,
    DateTime UploadedAtUtc);

public sealed record PlayerStatusHistoryDto(
    BookingStatus Status,
    string ChangedBy,
    string? Reason,
    DateTime ChangedAtUtc);

public sealed record GetPlayerBookingsQuery(
    Guid PlayerId,
    string? Tab,
    int Page = 1,
    int PageSize = 10
) : IQuery<Result<PagedList<PlayerBookingSummaryDto>>>;

public sealed record GetPlayerBookingByIdQuery(
    Guid BookingId,
    Guid PlayerId
) : IQuery<Result<PlayerBookingDetailDto>>;

public sealed class PlayerBookingQueriesHandler :
    IQueryHandler<GetPlayerBookingsQuery, Result<PagedList<PlayerBookingSummaryDto>>>,
    IQueryHandler<GetPlayerBookingByIdQuery, Result<PlayerBookingDetailDto>>
{
    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IVenueDirectory _venues;

    public PlayerBookingQueriesHandler(IBookingDbContext dbContext, IClock clock, IVenueDirectory venues)
    {
        _dbContext = dbContext;
        _clock = clock;
        _venues = venues;
    }

    public async Task<Result<PagedList<PlayerBookingSummaryDto>>> HandleAsync(
        GetPlayerBookingsQuery query,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var baseQuery = _dbContext.Bookings.AsNoTracking()
            .Where(b => b.PlayerId == query.PlayerId && !b.IsDeleted);

        // Apply Tab Filter
        baseQuery = (query.Tab?.ToLowerInvariant()) switch
        {
            "upcoming" => baseQuery.Where(b => (b.Status == BookingStatus.Confirmed || b.Status == BookingStatus.PendingDeposit || b.Status == BookingStatus.ProofSubmitted || b.Status == BookingStatus.UnderReview) && b.StartUtc >= nowUtc),
            "pending" => baseQuery.Where(b => b.Status == BookingStatus.PendingDeposit || b.Status == BookingStatus.ProofSubmitted || b.Status == BookingStatus.UnderReview),
            "completed" => baseQuery.Where(b => b.Status == BookingStatus.Completed || (b.Status == BookingStatus.Confirmed && b.EndUtc <= nowUtc)),
            "cancelled" => baseQuery.Where(b => b.Status == BookingStatus.Cancelled || b.Status == BookingStatus.Rejected || b.Status == BookingStatus.Expired || b.Status == BookingStatus.NoShow),
            _ => baseQuery // "all"
        };

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 50 ? 10 : query.PageSize;

        var items = await baseQuery
            .OrderByDescending(b => b.StartUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtos = items.Select(b =>
        {
            var allowed = ComputeAllowedActions(b, nowUtc);
            return new PlayerBookingSummaryDto(
                b.Id,
                b.BookingReference,
                b.VenueId,
                b.PitchId,
                b.StartUtc,
                b.EndUtc,
                b.TotalPrice.Amount,
                b.DepositAmount.Amount,
                b.TotalPrice.Currency,
                b.Status,
                b.HoldExpiresAtUtc,
                allowed,
                b.CreatedAtUtc);
        }).ToList();

        return Result.Success(new PagedList<PlayerBookingSummaryDto>(dtos, totalCount, page, pageSize));
    }

    public async Task<Result<PlayerBookingDetailDto>> HandleAsync(
        GetPlayerBookingByIdQuery query,
        CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;

        var booking = await _dbContext.Bookings.AsNoTracking()
            .Include(b => b.Proofs)
            .Include(b => b.History)
            .FirstOrDefaultAsync(b => b.Id == query.BookingId && !b.IsDeleted, cancellationToken);

        if (booking is null)
        {
            return Result.Failure<PlayerBookingDetailDto>(BookingErrors.NotFound);
        }

        // Someone else's booking looks like one that does not exist, so ids cannot be probed.
        if (booking.PlayerId != query.PlayerId)
        {
            return Result.Failure<PlayerBookingDetailDto>(BookingErrors.NotFound);
        }

        var summary = await _venues.GetVenueSummaryAsync(booking.VenueId, cancellationToken);

        var allowed = ComputeAllowedActions(booking, nowUtc);

        var proofs = booking.Proofs
            .OrderByDescending(p => p.UploadedAtUtc)
            .Select(p => new PlayerProofDto(
                p.Id,
                p.OriginalFileName,
                p.ContentType,
                p.FileSizeBytes,
                p.IsApproved,
                p.RejectionReason,
                p.UploadedAtUtc))
            .ToList();

        var history = booking.History
            .OrderBy(h => h.ChangedAtUtc)
            .Select(h => new PlayerStatusHistoryDto(
                h.Status,
                h.ChangedBy,
                h.Reason,
                h.ChangedAtUtc))
            .ToList();

        var dto = new PlayerBookingDetailDto(
            booking.Id,
            booking.BookingReference,
            booking.VenueId,
            booking.PitchId,
            booking.StartUtc,
            booking.EndUtc,
            booking.TotalPrice.Amount,
            booking.DepositAmount.Amount,
            booking.TotalPrice.Currency,
            booking.Status,
            booking.HoldExpiresAtUtc,
            booking.Notes,
            booking.PolicySnapshot?.Terms,
            summary is null ? null : new PlayerVenueDto(summary.Name, summary.Address, summary.Latitude, summary.Longitude),
            allowed,
            proofs,
            history,
            booking.CreatedAtUtc);

        return Result.Success(dto);
    }

    private static PlayerAllowedActions ComputeAllowedActions(Domain.Booking booking, DateTime nowUtc)
    {
        var canUploadProof = (booking.Status == BookingStatus.PendingDeposit ||
                              (booking.Status == BookingStatus.Rejected && booking.HoldExpiresAtUtc > nowUtc))
                             && booking.HoldExpiresAtUtc > nowUtc;

        var leadHours = booking.PolicySnapshot?.CancellationDeadlineHours ?? 24;
        var deadlineUtc = booking.StartUtc.AddHours(-leadHours);

        var canCancel = booking.Status is BookingStatus.PendingDeposit or BookingStatus.ProofSubmitted or BookingStatus.UnderReview ||
                        (booking.Status == BookingStatus.Confirmed && nowUtc <= deadlineUtc);

        var canReview = booking.Status == BookingStatus.Completed;

        return new PlayerAllowedActions(
            CanUploadProof: canUploadProof,
            CanCancel: canCancel,
            CanReview: canReview,
            CancellationDeadlineUtc: deadlineUtc.ToString("o"));
    }
}

public sealed record PagedList<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize);
