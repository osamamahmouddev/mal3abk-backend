using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Bookings.PaymentDetails;

public sealed record BookingPaymentMethodDto(
    Guid Id,
    string MethodType,
    string RecipientName,
    string AccountDetails,
    string? Instructions,
    bool IsOnline = false,
    bool IsAvailable = true,
    string? Badge = null);

public sealed record GetBookingPaymentMethodsQuery(Guid BookingId, Guid PlayerId) : IQuery<Result<IReadOnlyList<BookingPaymentMethodDto>>>;

/// <summary>
/// Where to send a booking's deposit: always the platform's own accounts, set by admins (commission model; venues no
/// longer receive deposits directly). Only the booking's player sees them, and only while the deposit can still be paid;
/// anyone else gets "not found". The platform keeps its commission and settles the rest with the venue.
/// </summary>
public sealed class GetBookingPaymentMethodsHandler : IQueryHandler<GetBookingPaymentMethodsQuery, Result<IReadOnlyList<BookingPaymentMethodDto>>>
{
    private static readonly Guid _platformPaymobId = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid _platformInstaPayId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid _platformWalletId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid _platformBankId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly IBookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly IPlatformSettings _platformSettings;

    public GetBookingPaymentMethodsHandler(IBookingDbContext dbContext, IClock clock, IPlatformSettings platformSettings)
    {
        _dbContext = dbContext;
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

        var recipient = await _platformSettings.GetStringAsync(PlatformSettingKeys.PlatformDepositRecipientName, cancellationToken);
        var instapay = await _platformSettings.GetStringAsync(PlatformSettingKeys.PlatformInstaPayAddress, cancellationToken);
        var wallet = await _platformSettings.GetStringAsync(PlatformSettingKeys.PlatformVodafoneCashNumber, cancellationToken);
        var bankAccount = await _platformSettings.GetStringAsync(PlatformSettingKeys.PlatformBankAccountIban, cancellationToken);
        var bankName = await _platformSettings.GetStringAsync(PlatformSettingKeys.PlatformBankName, cancellationToken);
        var instructions = await _platformSettings.GetStringAsync(PlatformSettingKeys.PlatformDepositInstructions, cancellationToken);

        var platformMethods = new List<BookingPaymentMethodDto>
        {
            new(
                _platformPaymobId,
                "Paymob",
                "بوابة Paymob للدفع الإلكتروني",
                "بطاقات بنكية (Visa / MasterCard / ميزة) ومحافظ إلكترونية",
                "الدفع الإلكتروني المباشر عبر Paymob غير متوفر حالياً وسيتاح قريباً. يرجى استخدام إنستاباي أو فودافون كاش أو التحويل البنكي.",
                IsOnline: true,
                IsAvailable: false,
                Badge: "قريباً")
        };

        if (!string.IsNullOrWhiteSpace(instapay))
        {
            platformMethods.Add(new BookingPaymentMethodDto(
                _platformInstaPayId,
                "InstaPay",
                recipient,
                instapay,
                instructions,
                IsOnline: false,
                IsAvailable: true,
                Badge: "فوري"));
        }
        if (!string.IsNullOrWhiteSpace(wallet))
        {
            platformMethods.Add(new BookingPaymentMethodDto(
                _platformWalletId,
                "MobileWallet",
                recipient,
                wallet,
                instructions,
                IsOnline: false,
                IsAvailable: true,
                Badge: "محفظة كاش"));
        }
        if (!string.IsNullOrWhiteSpace(bankAccount))
        {
            var bankDetails = !string.IsNullOrWhiteSpace(bankName) ? $"{bankName} - {bankAccount}" : bankAccount;
            platformMethods.Add(new BookingPaymentMethodDto(
                _platformBankId,
                "BankTransfer",
                recipient,
                bankDetails,
                instructions,
                IsOnline: false,
                IsAvailable: true,
                Badge: "تحويل بنكي"));
        }

        return Result.Success<IReadOnlyList<BookingPaymentMethodDto>>(platformMethods);
    }
}
