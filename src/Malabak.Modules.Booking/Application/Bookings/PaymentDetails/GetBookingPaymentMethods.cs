using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Bookings.PaymentDetails;

public sealed record BookingPaymentMethodDto(Guid Id, string MethodType, string RecipientName, string AccountDetails, string? Instructions);

public sealed record GetBookingPaymentMethodsQuery(Guid BookingId, Guid PlayerId) : IQuery<Result<IReadOnlyList<BookingPaymentMethodDto>>>;

/// <summary>
/// Where to send a booking's deposit. If platform deposit is enabled, returns the central platform payment accounts.
/// Only the booking's player sees it, and only while the deposit can still be paid; anyone else gets "not found".
/// </summary>
public sealed class GetBookingPaymentMethodsHandler : IQueryHandler<GetBookingPaymentMethodsQuery, Result<IReadOnlyList<BookingPaymentMethodDto>>>
{
    private static readonly Guid _platformInstaPayId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _platformWalletId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    private readonly IBookingDbContext _dbContext;
    private readonly IVenuePaymentDirectory _payments;
    private readonly IClock _clock;
    private readonly IPlatformSettings? _platformSettings;

    public GetBookingPaymentMethodsHandler(
        IBookingDbContext dbContext,
        IVenuePaymentDirectory payments,
        IClock clock,
        IPlatformSettings? platformSettings = null)
    {
        _dbContext = dbContext;
        _payments = payments;
        _clock = clock;
        _platformSettings = platformSettings;
    }

    public async Task<Result<IReadOnlyList<BookingPaymentMethodDto>>> HandleAsync(
        GetBookingPaymentMethodsQuery query, CancellationToken cancellationToken = default)
    {
        var booking = await _dbContext.Bookings.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == query.BookingId && !b.IsDeleted, cancellationToken);
        if (booking is null || booking.PlayerId != query.PlayerId)
        {
            return Result.Failure<IReadOnlyList<BookingPaymentMethodDto>>(BookingErrors.NotFound);
        }

        var awaitingDeposit = booking.Status is BookingStatus.PendingDeposit or BookingStatus.Rejected;
        if (!awaitingDeposit || booking.HoldExpiresAtUtc is not { } holdEnd || holdEnd <= _clock.UtcNow)
        {
            return Result.Failure<IReadOnlyList<BookingPaymentMethodDto>>(BookingErrors.HoldExpired);
        }

        var isPlatformDepositEnabled = _platformSettings is not null &&
            await _platformSettings.GetBoolAsync(PlatformSettingKeys.PlatformDepositEnabled, cancellationToken);
        if (isPlatformDepositEnabled && _platformSettings is not null)
        {
            var recipient = await _platformSettings.GetStringAsync(PlatformSettingKeys.PlatformDepositRecipientName, cancellationToken);
            var instapay = await _platformSettings.GetStringAsync(PlatformSettingKeys.PlatformInstaPayAddress, cancellationToken);
            var wallet = await _platformSettings.GetStringAsync(PlatformSettingKeys.PlatformVodafoneCashNumber, cancellationToken);
            var instructions = await _platformSettings.GetStringAsync(PlatformSettingKeys.PlatformDepositInstructions, cancellationToken);

            var platformMethods = new List<BookingPaymentMethodDto>();
            if (!string.IsNullOrWhiteSpace(instapay))
            {
                platformMethods.Add(new BookingPaymentMethodDto(_platformInstaPayId, "InstaPay", recipient, instapay, instructions));
            }
            if (!string.IsNullOrWhiteSpace(wallet))
            {
                platformMethods.Add(new BookingPaymentMethodDto(_platformWalletId, "MobileWallet", recipient, wallet, instructions));
            }

            if (platformMethods.Count > 0)
            {
                return Result.Success<IReadOnlyList<BookingPaymentMethodDto>>(platformMethods);
            }
        }

        var methods = await _payments.RevealForBookingAsync(booking.VenueId, booking.Id, query.PlayerId, cancellationToken);
        return Result.Success<IReadOnlyList<BookingPaymentMethodDto>>(methods
            .Select(m => new BookingPaymentMethodDto(m.Id, m.MethodType, m.RecipientName, m.AccountDetails, m.Instructions))
            .ToList());
    }
}
