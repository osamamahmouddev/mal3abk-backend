using Malabak.Modules.Booking.Domain;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Malabak.Api.Infrastructure.Seeding;

public sealed class DemoDataSeeder : IDataSeeder
{
    private readonly IHostEnvironment _environment;
    private readonly IdentityDbContext _identityDbContext;
    private readonly VenuesDbContext _venuesDbContext;
    private readonly BookingDbContext? _bookingDbContext;
    private readonly ILogger<DemoDataSeeder> _logger;

    public DemoDataSeeder(
        IHostEnvironment environment,
        IdentityDbContext identityDbContext,
        VenuesDbContext venuesDbContext,
        ILogger<DemoDataSeeder> logger,
        BookingDbContext? bookingDbContext = null)
    {
        _environment = environment;
        _identityDbContext = identityDbContext;
        _venuesDbContext = venuesDbContext;
        _logger = logger;
        _bookingDbContext = bookingDbContext;
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
                photos: ["https://assets.malabak.app/demo/ahly_main.jpg"],
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
                capacity: 10,
                lengthMeters: 40.0m,
                widthMeters: 20.0m,
                photos: ["https://assets.malabak.app/demo/pitch_5a.jpg"],
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
                capacity: 14,
                lengthMeters: 55.0m,
                widthMeters: 35.0m,
                photos: ["https://assets.malabak.app/demo/pitch_7a.jpg"],
                createdAtUtc: now);

            await _venuesDbContext.Pitches.AddAsync(pitch2, cancellationToken);
        }

        await _venuesDbContext.SaveChangesAsync(cancellationToken);
        return (venueAhly, pitch1, pitch2);
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
