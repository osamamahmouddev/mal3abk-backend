using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Application.Bookings.PlayerQueries;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Bookings.VenueQueries;

public sealed record VenueBookingItemDto(
    Guid Id,
    string Reference,
    Guid PitchId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal TotalPrice,
    decimal DepositAmount,
    string Currency,
    BookingStatus Status,
    string? CustomerName,
    string? CustomerPhone,
    DateTime CreatedAtUtc);

public sealed record VenueBookingDetailDto(
    Guid Id,
    string Reference,
    Guid VenueId,
    Guid PitchId,
    Guid PlayerId,
    DateTime StartUtc,
    DateTime EndUtc,
    decimal TotalPrice,
    decimal DepositAmount,
    string Currency,
    BookingStatus Status,
    DateTime? HoldExpiresAtUtc,
    string? CustomerName,
    string? CustomerPhone,
    string? Notes,
    IReadOnlyList<VenueProofItemDto> Proofs,
    IReadOnlyList<PlayerStatusHistoryDto> StatusHistory,
    DateTime CreatedAtUtc);

/// <summary>A payment proof as venue staff review it. The storage key is internal and never exposed; the image is fetched through <see cref="DownloadUrl"/>, a short-lived signed link the API adds.</summary>
public sealed record VenueProofItemDto(
    Guid Id,
    Guid? MediaId,
    string OriginalFileName,
    string ContentType,
    long FileSizeBytes,
    string Sha256Hash,
    bool? IsApproved,
    string? RejectionReason,
    bool IsScanClean,
    DateTime UploadedAtUtc,
    string? DownloadUrl = null);

public sealed record CalendarSlotDto(
    Guid Id,
    Guid PitchId,
    DateTime StartUtc,
    DateTime EndUtc,
    string Status,
    string? CustomerName,
    string BookingReference,
    bool IsManual);

public sealed record GetVenueBookingsQuery(
    Guid VenueId,
    BookingStatus? Status,
    DateTime? StartDateUtc,
    DateTime? EndDateUtc,
    Guid? PitchId,
    string? Search,
    int Page = 1,
    int PageSize = 20
) : IQuery<Result<PagedList<VenueBookingItemDto>>>;

public sealed record GetVenuePendingBookingsQuery(
    Guid VenueId
) : IQuery<Result<IReadOnlyList<VenueBookingItemDto>>>;

public sealed record GetVenueBookingDetailQuery(
    Guid VenueId,
    Guid BookingId
) : IQuery<Result<VenueBookingDetailDto>>;

public sealed record GetVenueCalendarQuery(
    Guid VenueId,
    DateTime RangeStartUtc,
    DateTime RangeEndUtc,
    Guid? PitchId
) : IQuery<Result<IReadOnlyList<CalendarSlotDto>>>;

public sealed class VenueBookingQueriesHandler :
    IQueryHandler<GetVenueBookingsQuery, Result<PagedList<VenueBookingItemDto>>>,
    IQueryHandler<GetVenuePendingBookingsQuery, Result<IReadOnlyList<VenueBookingItemDto>>>,
    IQueryHandler<GetVenueBookingDetailQuery, Result<VenueBookingDetailDto>>,
    IQueryHandler<GetVenueCalendarQuery, Result<IReadOnlyList<CalendarSlotDto>>>
{
    private readonly IBookingDbContext _dbContext;

    public VenueBookingQueriesHandler(IBookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<PagedList<VenueBookingItemDto>>> HandleAsync(
        GetVenueBookingsQuery query,
        CancellationToken cancellationToken = default)
    {
        var baseQuery = _dbContext.Bookings.AsNoTracking()
            .Where(b => b.VenueId == query.VenueId && !b.IsDeleted);

        if (query.Status.HasValue)
        {
            baseQuery = baseQuery.Where(b => b.Status == query.Status.Value);
        }

        if (query.StartDateUtc.HasValue)
        {
            baseQuery = baseQuery.Where(b => b.StartUtc >= query.StartDateUtc.Value);
        }

        if (query.EndDateUtc.HasValue)
        {
            baseQuery = baseQuery.Where(b => b.EndUtc <= query.EndDateUtc.Value);
        }

        if (query.PitchId.HasValue && query.PitchId.Value != Guid.Empty)
        {
            baseQuery = baseQuery.Where(b => b.PitchId == query.PitchId.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim().ToUpperInvariant();
            baseQuery = baseQuery.Where(b =>
                b.BookingReference.Contains(search) ||
                (b.CustomerName != null && b.CustomerName.ToUpper().Contains(search)));
        }

        var totalCount = await baseQuery.CountAsync(cancellationToken);
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        var items = await baseQuery
            .OrderByDescending(b => b.StartUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(b => new VenueBookingItemDto(
                b.Id,
                b.BookingReference,
                b.PitchId,
                b.StartUtc,
                b.EndUtc,
                b.TotalPrice.Amount,
                b.DepositAmount.Amount,
                b.TotalPrice.Currency,
                b.Status,
                b.CustomerName,
                b.CustomerPhone,
                b.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Result.Success(new PagedList<VenueBookingItemDto>(items, totalCount, page, pageSize));
    }

    public async Task<Result<IReadOnlyList<VenueBookingItemDto>>> HandleAsync(
        GetVenuePendingBookingsQuery query,
        CancellationToken cancellationToken = default)
    {
        var items = await _dbContext.Bookings.AsNoTracking()
            .Where(b => b.VenueId == query.VenueId
                     && (b.Status == BookingStatus.ProofSubmitted || b.Status == BookingStatus.PendingDeposit)
                     && !b.IsDeleted)
            .OrderBy(b => b.HoldExpiresAtUtc)
            .Select(b => new VenueBookingItemDto(
                b.Id,
                b.BookingReference,
                b.PitchId,
                b.StartUtc,
                b.EndUtc,
                b.TotalPrice.Amount,
                b.DepositAmount.Amount,
                b.TotalPrice.Currency,
                b.Status,
                b.CustomerName,
                b.CustomerPhone,
                b.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<VenueBookingItemDto>>(items);
    }

    public async Task<Result<VenueBookingDetailDto>> HandleAsync(
        GetVenueBookingDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        var booking = await _dbContext.Bookings.AsNoTracking()
            .Include(b => b.Proofs)
            .Include(b => b.History)
            .FirstOrDefaultAsync(b => b.Id == query.BookingId && b.VenueId == query.VenueId && !b.IsDeleted, cancellationToken);

        if (booking is null)
        {
            return Result.Failure<VenueBookingDetailDto>(BookingErrors.NotFound);
        }

        var proofs = booking.Proofs
            .OrderByDescending(p => p.UploadedAtUtc)
            .Select(p => new VenueProofItemDto(
                p.Id,
                p.MediaId,
                p.OriginalFileName,
                p.ContentType,
                p.FileSizeBytes,
                p.Sha256Hash,
                p.IsApproved,
                p.RejectionReason,
                p.IsScanClean,
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

        var dto = new VenueBookingDetailDto(
            booking.Id,
            booking.BookingReference,
            booking.VenueId,
            booking.PitchId,
            booking.PlayerId,
            booking.StartUtc,
            booking.EndUtc,
            booking.TotalPrice.Amount,
            booking.DepositAmount.Amount,
            booking.TotalPrice.Currency,
            booking.Status,
            booking.HoldExpiresAtUtc,
            booking.CustomerName,
            booking.CustomerPhone,
            booking.Notes,
            proofs,
            history,
            booking.CreatedAtUtc);

        return Result.Success(dto);
    }

    public async Task<Result<IReadOnlyList<CalendarSlotDto>>> HandleAsync(
        GetVenueCalendarQuery query,
        CancellationToken cancellationToken = default)
    {
        var activeStatuses = new[]
        {
            BookingStatus.PendingDeposit,
            BookingStatus.ProofSubmitted,
            BookingStatus.UnderReview,
            BookingStatus.Confirmed
        };

        var bookingsQuery = _dbContext.Bookings.AsNoTracking()
            .Where(b => b.VenueId == query.VenueId
                     && activeStatuses.Contains(b.Status)
                     && b.StartUtc < query.RangeEndUtc
                     && b.EndUtc > query.RangeStartUtc
                     && !b.IsDeleted);

        if (query.PitchId.HasValue && query.PitchId.Value != Guid.Empty)
        {
            bookingsQuery = bookingsQuery.Where(b => b.PitchId == query.PitchId.Value);
        }

        var bookings = await bookingsQuery
            .Select(b => new CalendarSlotDto(
                b.Id,
                b.PitchId,
                b.StartUtc,
                b.EndUtc,
                b.Status.ToString(),
                b.CustomerName,
                b.BookingReference,
                b.IsManual))
            .ToListAsync(cancellationToken);

        return Result.Success<IReadOnlyList<CalendarSlotDto>>(bookings);
    }
}
