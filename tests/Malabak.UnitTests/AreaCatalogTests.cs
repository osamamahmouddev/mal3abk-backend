using FluentAssertions;
using Malabak.Modules.Venues.Application.Areas;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Malabak.UnitTests;

public sealed class AreaCatalogTests
{
    private static readonly DateTime _now = new(2026, 11, 13, 10, 0, 0, DateTimeKind.Utc);
    private readonly Guid _user = Guid.NewGuid();

    [Fact]
    public async Task AnAddedArea_JoinsItsGovernorate_AfterTheBuiltInOnesAsync()
    {
        await using var db = NewDb();
        var service = new AreaCatalogService(db, new Clock());

        var added = await service.AddAsync("cairo", "  التجمع الخامس ", "Fifth Settlement", _user);

        added.IsSuccess.Should().BeTrue();
        var cairo = (await service.GovernoratesAsync()).Single(g => g.Code == "cairo");
        cairo.Areas[^1].NameAr.Should().Be("التجمع الخامس");
        cairo.Areas[^1].NameEn.Should().Be("Fifth Settlement");
        cairo.Areas.Should().Contain(a => a.NameAr == "المعادي", "the built-in areas stay");
        (await service.GovernoratesAsync()).Single(g => g.Code == "giza").Areas.Should().NotContain(a => a.NameAr == "التجمع الخامس");
    }

    [Fact]
    public async Task AnArea_NeedsAKnownGovernorateAndANewNameAsync()
    {
        await using var db = NewDb();
        var service = new AreaCatalogService(db, new Clock());

        (await service.AddAsync("atlantis", "المدينة", null, _user)).Error.Type.Should().Be(ErrorType.Validation);
        (await service.AddAsync("cairo", "   ", null, _user)).Error.Type.Should().Be(ErrorType.Validation);
        (await service.AddAsync("cairo", new string('ا', 61), null, _user)).Error.Type.Should().Be(ErrorType.Validation);
        (await service.AddAsync("cairo", "المعادي", null, _user)).Error.Type.Should().Be(ErrorType.Conflict, "it is already a built-in area");
        (await service.AddAsync("cairo", "التجمع", null, _user)).IsSuccess.Should().BeTrue();
        (await service.AddAsync("cairo", "التجمع", null, _user)).Error.Type.Should().Be(ErrorType.Conflict);
        (await service.AddAsync("giza", "التجمع", null, _user)).IsSuccess.Should().BeTrue("the same name can exist in another governorate");
    }

    [Fact]
    public async Task RemovingAnArea_TakesItOutOfTheListAndChangesTheVersionAsync()
    {
        await using var db = NewDb();
        var service = new AreaCatalogService(db, new Clock());
        var added = (await service.AddAsync("alexandria", "العصافرة", null, _user)).Value;
        var before = await service.VersionAsync();

        (await service.RemoveAsync(added.Id)).IsSuccess.Should().BeTrue();

        (await service.GovernoratesAsync()).Single(g => g.Code == "alexandria").Areas.Should().NotContain(a => a.NameAr == "العصافرة");
        (await service.VersionAsync()).Should().NotBe(before);
        (await service.RemoveAsync(added.Id)).Error.Type.Should().Be(ErrorType.NotFound);
    }

    private static VenuesDbContext NewDb() => new(new DbContextOptionsBuilder<VenuesDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private sealed class Clock : IClock
    {
        public DateTime UtcNow => _now;
        public DateTime CairoNow => _now;
        public DateOnly CairoToday => DateOnly.FromDateTime(_now);
        public TimeZoneInfo CairoTimeZone => TimeZoneInfo.Utc;
    }
}
