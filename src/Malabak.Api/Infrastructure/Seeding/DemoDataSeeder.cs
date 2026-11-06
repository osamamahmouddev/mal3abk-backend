using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Venues.Application.PaymentMethods;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Malabak.Api.Infrastructure.Seeding;

public sealed class DemoDataSeeder : IDataSeeder
{
    private const string _legacyDemoPhotoHost = "https://assets.malabak.app/demo/";

    private static readonly string[] _venuePhotos =
    [
        DemoPhoto("1431324155629-1a6deb1dec8d"),
        DemoPhoto("1556056504-5c7696c4c28d"),
        DemoPhoto("1551958219-acbc608c6377"),
        DemoPhoto("1529900748604-07564a03e7a6"),
        DemoPhoto("1459865264687-595d652de67e"),
    ];

    private static readonly string[] _fiveASidePhotos =
    [
        DemoPhoto("1575361204480-aadea25e6e68"),
        DemoPhoto("1574629810360-7efbbe195018"),
    ];

    private static readonly string[] _sevenASidePhotos =
    [
        DemoPhoto("1543326727-cf6c39e8f84c"),
        DemoPhoto("1522778119026-d647f0596c20"),
    ];

    private readonly IHostEnvironment _environment;
    private readonly IdentityDbContext _identityDbContext;
    private readonly VenuesDbContext _venuesDbContext;
    private readonly BookingDbContext? _bookingDbContext;
    private readonly IFieldCipher? _fieldCipher;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(
        IHostEnvironment environment,
        IdentityDbContext identityDbContext,
        VenuesDbContext venuesDbContext,
        ILogger<DemoDataSeeder> logger,
        BookingDbContext? bookingDbContext = null,
        IFieldCipher? fieldCipher = null)
    {
        _environment = environment;
        _identityDbContext = identityDbContext;
        _venuesDbContext = venuesDbContext;
        _logger = logger;
        _bookingDbContext = bookingDbContext;
        _fieldCipher = fieldCipher;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment() && !_environment.IsEnvironment("Testing"))
        {
            _logger.LogWarning("Demo data seeding is strictly blocked outside Development/Testing environments. Current: {Env}", _environment.EnvironmentName);
            return;
        }

        _logger.LogInformation("Starting Fictional Egyptian Demo Data Seeding in {Env}...", _environment.EnvironmentName);

        var (player1, player2, venueOwner) = await SeedDemoUsersAsync(cancellationToken);
        var (venueAhly, pitchAhly1, pitchAhly2) = await SeedDemoVenuesAndPitchesAsync(venueOwner.Id, cancellationToken);
        await MakeDemoVenueBookableAsync(venueAhly, pitchAhly2.Id, venueOwner.Id, cancellationToken);

        if (_bookingDbContext != null)
        {
            await SeedDemoBookingsAsync(venueAhly.Id, player1.Id, player2.Id, pitchAhly1.Id, pitchAhly2.Id, cancellationToken);
        }

        _logger.LogInformation("Demo Data Seeding completed successfully.");
    }

    private async Task<(User Player1, User Player2, User VenueOwner)> SeedDemoUsersAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;

        var player1 = await _identityDbContext.Users.FirstOrDefaultAsync(u => u.PhoneNumber == "+201012345678", cancellationToken);
        if (player1 == null)
        {
            player1 = User.CreatePlayer(
                id: Guid.Parse("11111111-1111-1111-1111-111111111111"),
                phoneNumber: "+201012345678",
                fullName: "عمرو زكي (Amr Zaki)",
                createdAtUtc: now);
            await _identityDbContext.Users.AddAsync(player1, cancellationToken);
        }

        var player2 = await _identityDbContext.Users.FirstOrDefaultAsync(u => u.PhoneNumber == "+201098765432", cancellationToken);
        if (player2 == null)
        {
            player2 = User.CreatePlayer(
                id: Guid.Parse("22222222-2222-2222-2222-222222222222"),
                phoneNumber: "+201098765432",
                fullName: "محمد زيدان (Mohamed Zidan)",
                createdAtUtc: now);
            await _identityDbContext.Users.AddAsync(player2, cancellationToken);
        }

        var venueOwner = await _identityDbContext.Users.FirstOrDefaultAsync(u => u.PhoneNumber == "+201100000001", cancellationToken);
        if (venueOwner == null)
        {
            venueOwner = User.CreateStaff(
                id: Guid.Parse("33333333-3333-3333-3333-333333333333"),
                email: "owner.ahly@malabak.app",
                phoneNumber: "+201100000001",
                passwordHash: "AQAAAAIAAYagAAAAEPwd+5...",
                fullName: "حسن شحاتة (Hassan Shehata)",
                role: UserRole.VenueOwner,
                createdAtUtc: now);
            await _identityDbContext.Users.AddAsync(venueOwner, cancellationToken);
        }

        await _identityDbContext.SaveChangesAsync(cancellationToken);
        return (player1, player2, venueOwner);
    }

    private async Task<(Venue VenueAhly, Pitch Pitch1, Pitch Pitch2)> SeedDemoVenuesAndPitchesAsync(
        Guid ownerId,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var ahlyVenueId = Guid.Parse("44444444-4444-4444-4444-444444444444");

        var venueAhly = await _venuesDbContext.Venues.FirstOrDefaultAsync(v => v.Id == ahlyVenueId, cancellationToken);
        if (venueAhly == null)
        {
            venueAhly = Venue.Create(
                id: ahlyVenueId,
                name: "ملاعب النادي الأهلي - مدينة نصر",
                slug: "al-ahly-club-nasr-city",
                description: "مجمع ملاعب خماسية وسباعية معتمدة بأحدث نجيل صناعي وكشافات ليلية",
                ownerId: ownerId,
                address: "شارع أحمد فخري، المنطقة السادسة، مدينة نصر",
                city: "Cairo",
                area: "Nasr City",
                latitude: 30.0561,
                longitude: 31.3301,
                phoneNumber: "+201000000001",
                facilities: ReferenceDataSeeder.StandardFacilities,
                photos: _venuePhotos,
                createdAtUtc: now);

            await _venuesDbContext.Venues.AddAsync(venueAhly, cancellationToken);
            await _venuesDbContext.SaveChangesAsync(cancellationToken);
        }

        var pitch1Id = Guid.Parse("55555555-5555-5555-5555-555555555551");
        var pitch1 = await _venuesDbContext.Pitches.FirstOrDefaultAsync(p => p.Id == pitch1Id, cancellationToken);
        if (pitch1 == null)
        {
            pitch1 = Pitch.Create(
                id: pitch1Id,
                venueId: ahlyVenueId,
                name: "ملعب التتش الصغير (خماسي)",
                turfType: SurfaceType.ArtificialTurfValue,
                capacity: 5,
                lengthMeters: 40.0m,
                widthMeters: 20.0m,
                photos: _fiveASidePhotos,
                createdAtUtc: now);

            await _venuesDbContext.Pitches.AddAsync(pitch1, cancellationToken);
        }

        var pitch2Id = Guid.Parse("55555555-5555-5555-5555-555555555552");
        var pitch2 = await _venuesDbContext.Pitches.FirstOrDefaultAsync(p => p.Id == pitch2Id, cancellationToken);
        if (pitch2 == null)
        {
            pitch2 = Pitch.Create(
                id: pitch2Id,
                venueId: ahlyVenueId,
                name: "ملعب النجوم (سباعي)",
                turfType: SurfaceType.ArtificialTurfValue,
                capacity: 7,
                lengthMeters: 55.0m,
                widthMeters: 35.0m,
                photos: _sevenASidePhotos,
                createdAtUtc: now);

            await _venuesDbContext.Pitches.AddAsync(pitch2, cancellationToken);
        }

        ReplaceLegacyPhotos(venueAhly.Photos, _venuePhotos, venueAhly.RemovePhoto, venueAhly.AddPhoto, now);
        ReplaceLegacyPhotos(pitch1.Photos, _fiveASidePhotos, pitch1.RemovePhoto, pitch1.AddPhoto, now);
        ReplaceLegacyPhotos(pitch2.Photos, _sevenASidePhotos, pitch2.RemovePhoto, pitch2.AddPhoto, now);

        await _venuesDbContext.SaveChangesAsync(cancellationToken);
        return (venueAhly, pitch1, pitch2);
    }

    /// <summary>Openly licensed football photos (Unsplash) for the demo venue gallery.</summary>
    private static string DemoPhoto(string unsplashId) =>
        $"https://images.unsplash.com/photo-{unsplashId}?w=1200&q=80&auto=format&fit=crop";

    /// <summary>Earlier seeds pointed at demo files that were never hosted; swap them for real photos.</summary>
    private static void ReplaceLegacyPhotos(
        IReadOnlyCollection<string> current,
        IEnumerable<string> replacements,
        Func<string, DateTime, bool> remove,
        Func<string, DateTime, bool> add,
        DateTime nowUtc)
    {
        var legacy = current.Where(url => url.StartsWith(_legacyDemoPhotoHost, StringComparison.Ordinal)).ToList();
        if (legacy.Count == 0)
        {
            return;
        }

        foreach (var url in legacy)
        {
            remove(url, nowUtc);
        }

        foreach (var url in replacements)
        {
            add(url, nowUtc);
        }
    }

    /// <summary>
    /// Gives the demo venue what a player needs to find and book it: opening hours, prices, a booking policy,
    /// a deposit payment method, and an approved, published listing. Each part is added only when missing.
    /// </summary>
    private async Task MakeDemoVenueBookableAsync(Venue venue, Guid largePitchId, Guid ownerId, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var venueId = venue.Id;

        if (!await _venuesDbContext.OperatingSchedules.AnyAsync(s => s.VenueId == venueId, cancellationToken))
        {
            var schedules = Enum.GetValues<DayOfWeek>()
                .Select(day => OperatingSchedule.Create(Guid.NewGuid(), venueId, day, new TimeOnly(9, 0), new TimeOnly(23, 0), 60, now))
                .ToList();
            await _venuesDbContext.OperatingSchedules.AddRangeAsync(schedules, cancellationToken);
        }

        if (!await _venuesDbContext.PriceRules.AnyAsync(r => r.VenueId == venueId, cancellationToken))
        {
            var rules = new[]
            {
                PriceRule.Create(venueId, new PriceRuleTerms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, null, "السعر الأساسي", 400m, null, null, null, null, null, null, null), now),
                PriceRule.Create(venueId, new PriceRuleTerms(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, largePitchId, "سعر الملعب السباعي", 600m, null, null, null, null, null, null, null), now),
            };
            foreach (var rule in rules)
            {
                if (rule.IsFailure)
                {
                    throw new InvalidOperationException($"Demo price rule is invalid: {rule.Error.Code}");
                }

                await _venuesDbContext.PriceRules.AddAsync(rule.Value, cancellationToken);
            }
        }

        var deposit = DepositRule.Create(DepositType.Percentage, 30m);

        if (!await _venuesDbContext.Policies.AnyAsync(p => p.VenueId == venueId, cancellationToken))
        {
            var terms = new BookingPolicyTerms(
                Deposit: deposit,
                ProofSubmissionWindowMinutes: 30,
                ManualReviewWindowMinutes: 60,
                CancellationDeadlineHours: 24,
                RefundEligibility: RefundEligibility.PartialRefund,
                RefundPercentage: 50m,
                NoShowRule: NoShowRule.ForfeitDeposit,
                LateArrivalGraceMinutes: 10,
                AllowReschedule: true,
                RescheduleDeadlineHours: 12,
                PendingBookingsBlockSlots: true,
                Terms: "الإلغاء قبل الموعد بـ 24 ساعة يسترد نصف العربون.");
            await _venuesDbContext.Policies.AddAsync(VenuePolicy.CreateVersion(venueId, null, terms, ownerId, now), cancellationToken);
        }

        // Account details are encrypted at rest, so the payment method needs the field cipher.
        if (_fieldCipher is not null && !await _venuesDbContext.PaymentMethods.AnyAsync(m => m.VenueId == venueId, cancellationToken))
        {
            var method = VenuePaymentMethod.Create(
                Guid.NewGuid(),
                venueId,
                PaymentMethodType.MobileWallet,
                "حسن شحاتة",
                "حوّل العربون وارفع صورة الإيصال.",
                deposit,
                now);
            const string walletNumber = "01000000001";
            var ciphertext = await _fieldCipher.EncryptAsync(walletNumber, PaymentMethodMapping.EncryptionContext(method.Id), cancellationToken);
            method.SetAccountDetails(ciphertext, walletNumber, now);
            await _venuesDbContext.PaymentMethods.AddAsync(method, cancellationToken);
        }

        if (venue.ApprovalStatus is VenueApprovalStatus.Draft)
        {
            var submitted = venue.SubmitForApproval(ownerId, null, now);
            var approved = venue.Approve(ownerId, "Demo data", now);
            await _venuesDbContext.ApprovalEvents.AddRangeAsync([submitted.Value, approved.Value], cancellationToken);
        }

        if (venue.IsApproved && !venue.IsPublished)
        {
            venue.SetPublished(true, now);
        }

        await _venuesDbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task SeedDemoBookingsAsync(
        Guid venueId,
        Guid player1Id,
        Guid player2Id,
        Guid pitch1Id,
        Guid pitch2Id,
        CancellationToken cancellationToken)
    {
        if (_bookingDbContext == null) return;

        var now = DateTime.UtcNow;

        // 1. Pending Deposit Booking
        var booking1Id = Guid.Parse("66666666-6666-6666-6666-666666666661");
        if (!await _bookingDbContext.Bookings.AnyAsync(b => b.Id == booking1Id, cancellationToken))
        {
            var booking1Result = Booking.Create(
                id: booking1Id,
                bookingReference: "BK-AHLY-PENDING",
                venueId: venueId,
                pitchId: pitch1Id,
                playerId: player1Id,
                startUtc: now.AddHours(3),
                endUtc: now.AddHours(4),
                totalPrice: Money.Create(450.00m, "EGP").Value,
                depositAmount: Money.Create(150.00m, "EGP").Value,
                holdExpiresAtUtc: now.AddMinutes(15),
                priceSnapshot: null,
                policySnapshot: null,
                nowUtc: now,
                actorUserId: player1Id.ToString());

            if (booking1Result.IsSuccess)
            {
                await _bookingDbContext.Bookings.AddAsync(booking1Result.Value, cancellationToken);
            }
        }

        // 2. Confirmed Booking
        var booking2Id = Guid.Parse("66666666-6666-6666-6666-666666666662");
        if (!await _bookingDbContext.Bookings.AnyAsync(b => b.Id == booking2Id, cancellationToken))
        {
            var booking2Result = Booking.Create(
                id: booking2Id,
                bookingReference: "BK-AHLY-CONFIRM",
                venueId: venueId,
                pitchId: pitch2Id,
                playerId: player2Id,
                startUtc: now.AddHours(5),
                endUtc: now.AddHours(6),
                totalPrice: Money.Create(700.00m, "EGP").Value,
                depositAmount: Money.Create(200.00m, "EGP").Value,
                holdExpiresAtUtc: now.AddMinutes(15),
                priceSnapshot: null,
                policySnapshot: null,
                nowUtc: now,
                actorUserId: player2Id.ToString());

            if (booking2Result.IsSuccess)
            {
                booking2Result.Value.SubmitProof(Guid.NewGuid(), player2Id.ToString(), now);
                booking2Result.Value.Confirm("SystemAdmin", now);
                await _bookingDbContext.Bookings.AddAsync(booking2Result.Value, cancellationToken);
            }
        }

        // 3. Cancelled Booking
        var booking3Id = Guid.Parse("66666666-6666-6666-6666-666666666663");
        if (!await _bookingDbContext.Bookings.AnyAsync(b => b.Id == booking3Id, cancellationToken))
        {
            var booking3Result = Booking.Create(
                id: booking3Id,
                bookingReference: "BK-AHLY-CANCEL",
                venueId: venueId,
                pitchId: pitch1Id,
                playerId: player1Id,
                startUtc: now.AddDays(1),
                endUtc: now.AddDays(1).AddHours(1),
                totalPrice: Money.Create(450.00m, "EGP").Value,
                depositAmount: Money.Create(150.00m, "EGP").Value,
                holdExpiresAtUtc: now.AddMinutes(15),
                priceSnapshot: null,
                policySnapshot: null,
                nowUtc: now,
                actorUserId: player1Id.ToString());

            if (booking3Result.IsSuccess)
            {
                booking3Result.Value.Cancel("Cancelled by player due to schedule conflict", player1Id.ToString(), isStaff: false, nowUtc: now);
                await _bookingDbContext.Bookings.AddAsync(booking3Result.Value, cancellationToken);
            }
        }

        await _bookingDbContext.SaveChangesAsync(cancellationToken);
    }
}
