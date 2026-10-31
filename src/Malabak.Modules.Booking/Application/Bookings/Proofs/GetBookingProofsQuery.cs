using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Bookings.Proofs;

public sealed record BookingProofDto(
    Guid Id,
    Guid BookingId,
    string OriginalFileName,
    string ContentType,
    long FileSizeBytes,
    string Sha256Hash,
    bool IsScanClean,
    bool? IsApproved,
    string? RejectionReason,
    DateTime UploadedAtUtc);

public sealed record GetBookingProofsQuery(Guid BookingId, Guid RequestingUserId) : IQuery<Result<IReadOnlyList<BookingProofDto>>>;

public sealed class GetBookingProofsHandler : IQueryHandler<GetBookingProofsQuery, Result<IReadOnlyList<BookingProofDto>>>
{
    private readonly IBookingDbContext _dbContext;

    public GetBookingProofsHandler(IBookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<BookingProofDto>>> HandleAsync(
        GetBookingProofsQuery query,
        CancellationToken cancellationToken = default)
    {
        var booking = await _dbContext.Bookings.AsNoTracking()
            .Include(b => b.Proofs)
            .FirstOrDefaultAsync(b => b.Id == query.BookingId && !b.IsDeleted, cancellationToken);

        if (booking is null)
        {
            return Result.Failure<IReadOnlyList<BookingProofDto>>(BookingErrors.NotFound);
        }

        // Payment receipts are private. Only the booking's player reads them here; venue staff review proofs through the
        // venue-manager booking detail, which is scoped to their own venue. A booking that is not the caller's looks like
        // one that does not exist, so ids cannot be probed.
        if (booking.PlayerId != query.RequestingUserId)
        {
            return Result.Failure<IReadOnlyList<BookingProofDto>>(BookingErrors.NotFound);
        }

        var proofs = booking.Proofs
            .OrderByDescending(p => p.UploadedAtUtc)
            .Select(p => new BookingProofDto(
                p.Id,
                p.BookingId,
                p.OriginalFileName,
                p.ContentType,
                p.FileSizeBytes,
                p.Sha256Hash,
                p.IsScanClean,
                p.IsApproved,
                p.RejectionReason,
                p.UploadedAtUtc))
            .ToList();

        return Result.Success<IReadOnlyList<BookingProofDto>>(proofs);
    }
}
