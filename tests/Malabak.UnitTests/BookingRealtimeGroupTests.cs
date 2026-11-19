using FluentAssertions;
using Malabak.Api.Infrastructure.Realtime;
using Malabak.SharedKernel.Time;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Players watch a venue's day in Cairo. A slot's live update must reach that room, including slots after
/// midnight that UTC puts on the previous day.
/// </summary>
public sealed class BookingRealtimeGroupTests
{
    private static readonly Guid _venueId = Guid.Parse("9d6f1c1e-4b0a-4a35-9d7e-0d6a2b4c8e11");
    private static readonly TimeZoneInfo _cairo = new FakeClock(DateTime.UtcNow).CairoTimeZone;

    private static string Room(int year, int month, int day) =>
        BookingHub.VenueDateGroupName(_venueId, new DateOnly(year, month, day));

    [Fact]
    public void EveningSlot_GoesToItsCairoDay()
    {
        // 18:00 UTC is 20:00 in Cairo (UTC+2 in November).
        var groups = BookingRealtimeBroadcaster.SlotGroupNames(_venueId, new DateTime(2026, 11, 17, 18, 0, 0, DateTimeKind.Utc), _cairo);

        groups.Should().Contain(Room(2026, 11, 17));
    }

    [Fact]
    public void SlotAfterMidnight_ReachesTheCairoDayThatUtcPutsOnTheDayBefore()
    {
        // 23:00 UTC on the 16th is 01:00 on the 17th in Cairo.
        var groups = BookingRealtimeBroadcaster.SlotGroupNames(_venueId, new DateTime(2026, 11, 16, 23, 0, 0, DateTimeKind.Utc), _cairo);

        groups.Should().Contain(Room(2026, 11, 17), "players opening the 17th see this slot");
        groups.Should().Contain(Room(2026, 11, 16), "a venue open past midnight lists it under the 16th");
    }

    [Fact]
    public void Slot_GoesToExactlyTheCairoDayAndTheDayBefore()
    {
        var groups = BookingRealtimeBroadcaster.SlotGroupNames(_venueId, new DateTime(2026, 11, 17, 10, 0, 0, DateTimeKind.Utc), _cairo);

        groups.Should().BeEquivalentTo(Room(2026, 11, 17), Room(2026, 11, 16));
    }
}
