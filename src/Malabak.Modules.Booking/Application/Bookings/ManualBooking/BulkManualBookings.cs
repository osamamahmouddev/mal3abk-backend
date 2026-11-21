using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Application.Bookings.CancelBooking;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Bookings.ManualBooking;

public sealed record BulkSlot(Guid PitchId, DateTime StartUtc, DateTime EndUtc);

public sealed record BulkSlotResult(Guid PitchId, DateTime StartUtc, DateTime EndUtc, bool Succeeded, Guid? BookingId, string? ErrorCode, string? Error);

public sealed record BulkManualBookingResult(int Succeeded, int Failed, IReadOnlyList<BulkSlotResult> Slots);

public sealed record BulkReleaseItemResult(Guid BookingId, bool Succeeded, string? ErrorCode, string? Error);

public sealed record BulkReleaseResult(int Succeeded, int Failed, IReadOnlyList<BulkReleaseItemResult> Items);

/// <summary>
/// The venue marks many hours at once from its calendar: as booked (confirmed, or tentative until it confirms) so the app shows
/// them taken, or back to free so players can book them. Consecutive hours on the same pitch become one booking. Every hour goes
/// through the normal manual-booking rules (overlap protection included), so one taken hour never blocks the rest.
/// </summary>
public sealed class BulkManualBookingService
{
    public const int MaxSlots = 120;
    public const string ReleaseReason = "الميعاد اتفتح للحجز من جديد";

    private static readonly BookingStatus[] _active =
        [BookingStatus.PendingDeposit, BookingStatus.ProofSubmitted, BookingStatus.UnderReview, BookingStatus.Confirmed];

    private readonly ICommandHandler<CreateManualBookingCommand, Result<ManualBookingResponse>> _create;
    private readonly ICommandHandler<VenueCancelBookingCommand, Result> _cancel;
    private readonly IBookingDbContext _dbContext;
    private readonly IPriceQuoteProvider _prices;

    public BulkManualBookingService(
        ICommandHandler<CreateManualBookingCommand, Result<ManualBookingResponse>> create,
        ICommandHandler<VenueCancelBookingCommand, Result> cancel,
        IBookingDbContext dbContext,
        IPriceQuoteProvider prices)
    {
        _create = create;
        _cancel = cancel;
        _dbContext = dbContext;
        _prices = prices;
    }

    public async Task<Result<BulkManualBookingResult>> BookAsync(
        Guid venueId, Guid actorId, IReadOnlyList<BulkSlot> slots, ManualPaymentStatus status, string? customerName, string? customerPhone,
        string? notes, decimal? pricePerHour, CancellationToken cancellationToken = default)
    {
        if (slots.Count is 0 or > MaxSlots || slots.Any(s => s.EndUtc <= s.StartUtc || s.EndUtc - s.StartUtc > TimeSpan.FromHours(24)))
        {
            return Result.Failure<BulkManualBookingResult>(Error.Validation("Booking.BulkInvalidSlots", $"Choose between 1 and {MaxSlots} valid time slots."));
        }

        // Many hours at once are either fully paid (confirmed) or tentative; a deposit amount is a single booking's business.
        if (status is not (ManualPaymentStatus.FullyPaid or ManualPaymentStatus.Unconfirmed))
        {
            return Result.Failure<BulkManualBookingResult>(Error.Validation("Booking.BulkInvalidStatus", "Mark the hours as confirmed or tentative."));
        }

        if (pricePerHour is <= 0 or > 100_000)
        {
            return Result.Failure<BulkManualBookingResult>(Error.Validation("Booking.BulkInvalidPrice", "The price per hour must be more than zero."));
        }

        var results = new List<BulkSlotResult>();
        foreach (var slot in Merge(slots))
        {
            decimal price;
            if (pricePerHour is { } perHour)
            {
                price = decimal.Round(perHour * (decimal)(slot.EndUtc - slot.StartUtc).TotalHours, 2);
            }
            else
            {
                var quote = await _prices.QuoteAsync(slot.PitchId, slot.StartUtc, slot.EndUtc, cancellationToken);
                if (quote.IsFailure || quote.Value.Total <= 0)
                {
                    results.Add(new BulkSlotResult(slot.PitchId, slot.StartUtc, slot.EndUtc, false, null, "Booking.PriceUnavailable",
                        "No price is set for this time: enter the price per hour."));
                    continue;
                }

                price = quote.Value.Total;
            }

            var created = await _create.HandleAsync(new CreateManualBookingCommand(
                venueId, slot.PitchId, slot.StartUtc, slot.EndUtc, price, "EGP", customerName, customerPhone, actorId.ToString("D"), notes, status),
                cancellationToken);
            results.Add(created.IsSuccess
                ? new BulkSlotResult(slot.PitchId, slot.StartUtc, slot.EndUtc, true, created.Value.BookingId, null, null)
                : new BulkSlotResult(slot.PitchId, slot.StartUtc, slot.EndUtc, false, null, created.Error.Code, created.Error.Description));
        }

        return Result.Success(new BulkManualBookingResult(results.Count(r => r.Succeeded), results.Count(r => !r.Succeeded), results));
    }

    /// <summary>Frees hours the venue had marked as booked by hand. Bookings players made in the app are not cancelled this way.</summary>
    public async Task<Result<BulkReleaseResult>> ReleaseAsync(Guid venueId, Guid actorId, IReadOnlyList<Guid> bookingIds, CancellationToken cancellationToken = default)
    {
        var ids = bookingIds.Distinct().ToList();
        if (ids.Count is 0 or > MaxSlots)
        {
            return Result.Failure<BulkReleaseResult>(Error.Validation("Booking.BulkInvalidSlots", $"Choose between 1 and {MaxSlots} bookings."));
        }

        var bookings = await _dbContext.Bookings.AsNoTracking()
            .Where(b => ids.Contains(b.Id) && b.VenueId == venueId && !b.IsDeleted)
            .Select(b => new { b.Id, b.IsManualBooking, b.Status })
            .ToDictionaryAsync(b => b.Id, cancellationToken);

        var results = new List<BulkReleaseItemResult>();
        foreach (var id in ids)
        {
            if (!bookings.TryGetValue(id, out var booking))
            {
                results.Add(new BulkReleaseItemResult(id, false, BookingErrors.NotFound.Code, BookingErrors.NotFound.Description));
                continue;
            }

            if (!booking.IsManualBooking || !_active.Contains(booking.Status))
            {
                results.Add(new BulkReleaseItemResult(id, false, "Booking.NotReleasable",
                    "Only active bookings the venue entered by hand can be freed here; a player's booking is cancelled on its own."));
                continue;
            }

            var cancelled = await _cancel.HandleAsync(new VenueCancelBookingCommand(id, venueId, actorId, ReleaseReason), cancellationToken);
            results.Add(cancelled.IsSuccess
                ? new BulkReleaseItemResult(id, true, null, null)
                : new BulkReleaseItemResult(id, false, cancelled.Error.Code, cancelled.Error.Description));
        }

        return Result.Success(new BulkReleaseResult(results.Count(r => r.Succeeded), results.Count(r => !r.Succeeded), results));
    }

    /// <summary>Back-to-back hours on the same pitch become one booking (8–9 and 9–10 become 8–10).</summary>
    internal static List<BulkSlot> Merge(IEnumerable<BulkSlot> slots)
    {
        var merged = new List<BulkSlot>();
        foreach (var group in slots.Distinct().GroupBy(s => s.PitchId))
        {
            BulkSlot? current = null;
            foreach (var slot in group.OrderBy(s => s.StartUtc))
            {
                if (current is not null && slot.StartUtc <= current.EndUtc)
                {
                    current = current with { EndUtc = slot.EndUtc > current.EndUtc ? slot.EndUtc : current.EndUtc };
                    continue;
                }

                if (current is not null)
                {
                    merged.Add(current);
                }

                current = slot;
            }

            if (current is not null)
            {
                merged.Add(current);
            }
        }

        return merged;
    }
}
