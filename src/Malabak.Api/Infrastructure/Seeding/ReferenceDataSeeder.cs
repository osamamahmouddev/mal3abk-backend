using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Venues.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Api.Infrastructure.Seeding;

public sealed class ReferenceDataSeeder : IDataSeeder
{
    private readonly IdentityDbContext _identityDbContext;
    private readonly ILogger<ReferenceDataSeeder> _logger;

    public static readonly IReadOnlyList<string> StandardFacilities =
    [
        "Floodlights",
        "Parking",
        "Locker Rooms",
        "Cafeteria",
        "Shower",
        "Wi-Fi",
        "Ball Rental",
        "Bibs / Pinnies",
        "Referee On-Demand",
        "First Aid Station"
    ];

    public static readonly IReadOnlyList<string> EgyptianGovernorates =
    [
        "Cairo",
        "Giza",
        "Alexandria",
        "Dakahlia",
        "Gharbia",
        "Port Said",
        "Suez",
        "Ismailia",
        "Aswan",
        "Luxor",
        "Red Sea",
        "South Sinai"
    ];

    public ReferenceDataSeeder(
        IdentityDbContext identityDbContext,
        ILogger<ReferenceDataSeeder> logger)
    {
        _identityDbContext = identityDbContext;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting Reference Data Seeding...");

        await SeedSystemAdminAsync(cancellationToken);

        _logger.LogInformation("Reference Data Seeding completed successfully.");
    }

    private async Task SeedSystemAdminAsync(CancellationToken cancellationToken)
    {
        const string adminEmail = "admin@malabak.app";
        const string adminPhone = "+201000000000";

        var adminExists = await _identityDbContext.Users
            .AnyAsync(u => u.Email == adminEmail || u.PhoneNumber == adminPhone, cancellationToken);

        if (!adminExists)
        {
            var adminUser = User.CreateStaff(
                id: Guid.Parse("00000000-0000-0000-0000-000000000001"),
                email: adminEmail,
                phoneNumber: adminPhone,
                passwordHash: "AQAAAAIAAYagAAAAEPwd+5...",
                fullName: "Malabak System Administrator",
                role: UserRole.Admin,
                createdAtUtc: DateTime.UtcNow);

            await _identityDbContext.Users.AddAsync(adminUser, cancellationToken);
            await _identityDbContext.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Seeded default system administrator: {Email}", adminEmail);
        }
    }
}
