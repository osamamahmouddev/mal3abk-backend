using FluentAssertions;
using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Application.Pricing;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class PriceRuleManagementTests
{
    private static readonly TimeZoneInfo _cairo = TimeZoneInfo.FindSystemTimeZoneById("Africa/Cairo");
    private static readonly DateTime _now = new(2026, 11, 1, 10, 0, 0, DateTimeKind.Utc);

    private readonly Guid _venueId = Guid.NewGuid();
    private readonly Guid _pitchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly RecordingAudit _audit = new();

    private static DateTime Local(int day, int hour) =>
        TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, 11, day, hour, 0, 0, DateTimeKind.Unspecified), _cairo);

    [Fact]
    public async Task Create_List_Update_Delete_AreAuditedAsync()
    {
        await using var db = await SeedAsync();
        var rules = Management(db, VenueAccess.Owner(_venueId));

        var created = await rules.CreateAsync(_venueId, _userId, BaseTerms(400));
        var updated = await rules.UpdateAsync(_venueId, _userId, created.Value.Id, BaseTerms(450), isActive: true);
        (await rules.ListAsync(_venueId, _userId)).Value.Should().ContainSingle().Which.Amount.Should().Be(450);
        (await rules.DeleteAsync(_venueId, _userId, created.Value.Id)).IsSuccess.Should().BeTrue();

        updated.Value.Amount.Should().Be(450);
        (await rules.ListAsync(_venueId, _userId)).Value.Should().BeEmpty();
        _audit.Actions.Should().Equal("PriceRuleCreated", "PriceRuleUpdated", "PriceRuleDeleted");
    }

    [Fact]
    public async Task OverlappingRules_OfTheSameKindAndScope_AreRejectedAsync()
    {
        await using var db = await SeedAsync();
        var rules = Management(db, VenueAccess.Owner(_venueId));
        await rules.CreateAsync(_venueId, _userId, BaseTerms(400));

        (await rules.CreateAsync(_venueId, _userId, BaseTerms(500))).Error.Should().Be(PriceRuleErrors.Overlap);
        (await rules.CreateAsync(_venueId, _userId, BaseTerms(500, _pitchId))).IsSuccess.Should().BeTrue("a pitch override is a different scope");

        await rules.CreateAsync(_venueId, _userId, PeakTerms(new TimeOnly(18, 0), new TimeOnly(22, 0)));
        (await rules.CreateAsync(_venueId, _userId, PeakTerms(new TimeOnly(21, 0), new TimeOnly(23, 0)))).Error.Should().Be(PriceRuleErrors.Overlap);
        (await rules.CreateAsync(_venueId, _userId, PeakTerms(new TimeOnly(8, 0), new TimeOnly(10, 0)))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task RulesPerVenue_AreCappedAsync()
    {
        await using var db = await SeedAsync();
        var rules = Management(db, VenueAccess.Owner(_venueId));
        for (var i = 0; i < PriceRule.MaxRulesPerVenue; i++)
        {
            db.PriceRules.Add(PriceRule.Create(_venueId, DiscountTerms(10), DateTime.UtcNow).Value);
        }

        await db.SaveChangesAsync();

        (await rules.CreateAsync(_venueId, _userId, DiscountTerms(5))).Error.Should().Be(PriceRuleErrors.TooMany);
    }

    [Fact]
    public async Task Discounts_MayOverlapAsync()
    {
        await using var db = await SeedAsync();
        var rules = Management(db, VenueAccess.Owner(_venueId));

        (await rules.CreateAsync(_venueId, _userId, DiscountTerms(10))).IsSuccess.Should().BeTrue();
        (await rules.CreateAsync(_venueId, _userId, DiscountTerms(15))).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task UsersWithoutEditPermission_OrOtherVenuesPitch_AreRejectedAsync()
    {
        await using var db = await SeedAsync();

        (await Management(db, VenueAccess.None(_venueId)).CreateAsync(_venueId, _userId, BaseTerms(400)))
            .Error.Should().Be(VenueAccessErrors.VenueNotFound);
        (await Management(db, VenueAccess.Owner(_venueId)).CreateAsync(_venueId, _userId, BaseTerms(400, Guid.NewGuid())))
            .Error.Should().Be(VenueAccessErrors.PitchNotFound);
    }

    [Fact]
    public async Task Preview_ComparesSavedRulesWithADraftAsync()
    {
        await using var db = await SeedAsync();
        var rules = Management(db, VenueAccess.Owner(_venueId));
        await rules.CreateAsync(_venueId, _userId, BaseTerms(400));

        var preview = await rules.PreviewAsync(_venueId, _userId, _pitchId, Local(5, 19), Local(5, 21), PeakTerms(new TimeOnly(18, 0), new TimeOnly(23, 0)));

        preview.Value.Current.Total.Should().Be(800m);
        preview.Value.WithDraft!.Total.Should().Be(1000m);
        preview.Value.Difference.Should().Be(200m);
        (await db.PriceRules.CountAsync()).Should().Be(1, "the draft is never saved");
    }

    [Fact]
    public async Task Preview_WithoutBasePrice_ExplainsWhyAsync()
    {
        await using var db = await SeedAsync();

        var preview = await Management(db, VenueAccess.Owner(_venueId)).PreviewAsync(_venueId, _userId, _pitchId, Local(5, 19), Local(5, 20), null);

        preview.Error.Should().Be(PriceRuleErrors.NoBasePrice);
    }

    private PriceRuleManagement Management(VenuesDbContext db, VenueAccess access)
    {
        var clock = new FixedClock();
        return new PriceRuleManagement(db, new FakeAccess(access), _audit, new PricingService(db, clock), clock);
    }

    private async Task<VenuesDbContext> SeedAsync()
    {
        var db = new VenuesDbContext(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Pitches.Add(Pitch.Create(_pitchId, _venueId, "P1", SurfaceType.ArtificialTurfValue, 5, null, null, null, _now));
        await db.SaveChangesAsync();
        return db;
    }

    private static PriceRuleTerms BaseTerms(decimal amount, Guid? pitchId = null) =>
        new(PriceRuleKind.Base, PriceAdjustment.FixedHourlyRate, pitchId, "Base", amount, null, null, null, null, null, null, null);

    private static PriceRuleTerms PeakTerms(TimeOnly start, TimeOnly end) =>
        new(PriceRuleKind.Peak, PriceAdjustment.Percentage, null, "Peak", null, 25, null, start, end, null, null, null);

    private static PriceRuleTerms DiscountTerms(decimal percent) =>
        new(PriceRuleKind.Discount, PriceAdjustment.Percentage, null, $"{percent}% off", null, percent, null, null, null, null, null, 120);

    private sealed class RecordingAudit : IVenueAuditLogger
    {
        public List<string> Actions { get; } = [];

        public Task LogAsync(string action, string entityName, Guid entityId, Guid userId, object? metadata = null, CancellationToken cancellationToken = default)
        {
            Actions.Add(action);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAccess(VenueAccess access) : IVenueAccessService
    {
        public Task<VenueAccess> GetAccessAsync(Guid userId, Guid venueId, CancellationToken cancellationToken = default) => Task.FromResult(access);

        public Task<IReadOnlyList<VenueAccess>> GetAccessibleVenuesAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VenueAccess>>([access]);
    }

    private sealed class FixedClock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => TimeZoneInfo.ConvertTimeFromUtc(_now, _cairo);
        public DateOnly CairoToday => DateOnly.FromDateTime(CairoNow);
        public TimeZoneInfo CairoTimeZone => _cairo;
    }
}
