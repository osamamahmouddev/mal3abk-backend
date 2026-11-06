using System.Text;
using FluentAssertions;
using Malabak.Api.Infrastructure.Seeding;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Malabak.IntegrationTests.Infrastructure;

public sealed class DataSeederIntegrationTests
{
    [Fact]
    public async Task ReferenceDataSeeder_ExecutesIdempotentlyAndSeedsAdminAsync()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        await using var identityDb = new IdentityDbContext(identityOptions);
        var seeder = new ReferenceDataSeeder(identityDb, NullLogger<ReferenceDataSeeder>.Instance);

        // Act - First Execution
        await seeder.SeedAsync();

        // Assert
        var admin = await identityDb.Users.FirstOrDefaultAsync(u => u.Email == "admin@malabak.app");
        admin.Should().NotBeNull();
        admin!.FullName.Should().Be("Malabak System Administrator");
        admin.Roles.Should().Contain(UserRole.Admin);

        // Act - Second Execution (Idempotency)
        Func<Task> secondRun = async () => await seeder.SeedAsync();
        await secondRun.Should().NotThrowAsync();

        var adminCount = await identityDb.Users.CountAsync(u => u.Email == "admin@malabak.app");
        adminCount.Should().Be(1);
    }

    [Fact]
    public async Task DemoDataSeeder_ExecutesInDevelopmentAndPopulatesVenuesAndBookingsAsync()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        await using var identityDb = new IdentityDbContext(identityOptions);

        var venuesOptions = new DbContextOptionsBuilder<VenuesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        await using var venuesDb = new VenuesDbContext(venuesOptions);

        var bookingOptions = new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        await using var bookingDb = new BookingDbContext(bookingOptions);

        var env = new TestHostEnvironment("Development");
        var seeder = new DemoDataSeeder(
            env,
            identityDb,
            venuesDb,
            NullLogger<DemoDataSeeder>.Instance,
            bookingDb,
            new PlainTextCipher());

        // Act - First run
        await seeder.SeedAsync();

        // Assert - Demo Users
        var player1 = await identityDb.Users.FirstOrDefaultAsync(u => u.PhoneNumber == "+201012345678");
        player1.Should().NotBeNull();
        player1!.FullName.Should().Contain("عمرو زكي");

        var venueOwner = await identityDb.Users.FirstOrDefaultAsync(u => u.PhoneNumber == "+201100000001");
        venueOwner.Should().NotBeNull();
        venueOwner!.Roles.Should().Contain(UserRole.VenueOwner);

        // Assert - Demo Venue & Pitches
        var venue = await venuesDb.Venues.FirstOrDefaultAsync(v => v.Slug == "al-ahly-club-nasr-city");
        venue.Should().NotBeNull();
        venue!.City.Should().Be("Cairo");

        var pitches = await venuesDb.Pitches.Where(p => p.VenueId == venue.Id).ToListAsync();
        pitches.Should().HaveCount(2);

        // Assert - Demo venue is bookable by players
        venue.ApprovalStatus.Should().Be(VenueApprovalStatus.Approved);
        venue.IsPublished.Should().BeTrue();
        venue.Photos.Should().HaveCount(5).And.OnlyContain(url => url.StartsWith("https://images.unsplash.com/", StringComparison.Ordinal));
        (await venuesDb.OperatingSchedules.CountAsync(s => s.VenueId == venue.Id)).Should().Be(7);
        (await venuesDb.PriceRules.CountAsync(r => r.VenueId == venue.Id)).Should().Be(2);
        (await venuesDb.Policies.CountAsync(p => p.VenueId == venue.Id && p.IsCurrent)).Should().Be(1);
        (await venuesDb.PaymentMethods.CountAsync(m => m.VenueId == venue.Id)).Should().Be(1);

        // Assert - Demo Bookings
        var bookings = await bookingDb.Bookings.ToListAsync();
        bookings.Should().HaveCount(3);

        // Act - Second run (Idempotency)
        Func<Task> idempotentRun = async () => await seeder.SeedAsync();
        await idempotentRun.Should().NotThrowAsync();
    }

    [Fact]
    public async Task DemoDataSeeder_BlocksExecutionInProductionAsync()
    {
        // Arrange
        var dbName = Guid.NewGuid().ToString();
        var identityOptions = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        await using var identityDb = new IdentityDbContext(identityOptions);

        var venuesOptions = new DbContextOptionsBuilder<VenuesDbContext>()
            .UseInMemoryDatabase(dbName)
            .Options;
        await using var venuesDb = new VenuesDbContext(venuesOptions);

        var prodEnv = new TestHostEnvironment("Production");

        var seeder = new DemoDataSeeder(
            prodEnv,
            identityDb,
            venuesDb,
            NullLogger<DemoDataSeeder>.Instance);

        // Act
        await seeder.SeedAsync();

        // Assert - Production environment blocks seeding
        prodEnv.IsProduction().Should().BeTrue();
        var usersCount = await identityDb.Users.CountAsync();
        usersCount.Should().Be(0);
    }

    private sealed class PlainTextCipher : IFieldCipher
    {
        public ValueTask<byte[]> EncryptAsync(string plaintext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Encoding.UTF8.GetBytes(plaintext));

        public ValueTask<string> DecryptAsync(byte[] ciphertext, FieldEncryptionContext context, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Encoding.UTF8.GetString(ciphertext));

        public ValueTask<bool> NeedsReEncryptionAsync(byte[] ciphertext, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(false);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public TestHostEnvironment(string environmentName)
        {
            EnvironmentName = environmentName;
            ApplicationName = "Malabak.Api";
            ContentRootPath = AppContext.BaseDirectory;
            ContentRootFileProvider = new NullFileProvider();
        }

        public string EnvironmentName { get; set; }
        public string ApplicationName { get; set; }
        public string ContentRootPath { get; set; }
        public IFileProvider ContentRootFileProvider { get; set; }
    }
}
