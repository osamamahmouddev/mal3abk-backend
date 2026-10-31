using FluentAssertions;
using Malabak.Modules.Booking.Domain;
using Xunit;

namespace Malabak.UnitTests;

/// <summary>
/// Statuses are stored as integers, and the <c>no_overlapping_active_bookings</c> exclusion constraint (migration
/// AddNoOverlappingActiveBookingsConstraint) lists the slot-holding ones by value. Reordering the enum would silently
/// change which bookings the database protects, so the values are pinned here.
/// </summary>
public sealed class BookingStatusPersistenceTests
{
    [Theory]
    [InlineData(BookingStatus.PendingDeposit, 2)]
    [InlineData(BookingStatus.ProofSubmitted, 3)]
    [InlineData(BookingStatus.UnderReview, 4)]
    [InlineData(BookingStatus.Confirmed, 5)]
    public void SlotHoldingStatuses_KeepTheValuesTheDatabaseConstraintUses(BookingStatus status, int expected)
    {
        ((int)status).Should().Be(expected);
    }

    [Theory]
    [InlineData(BookingStatus.Draft, 1)]
    [InlineData(BookingStatus.Rejected, 6)]
    [InlineData(BookingStatus.Expired, 7)]
    [InlineData(BookingStatus.Cancelled, 8)]
    [InlineData(BookingStatus.Completed, 9)]
    [InlineData(BookingStatus.NoShow, 10)]
    public void OtherStatuses_KeepTheirStoredValues(BookingStatus status, int expected)
    {
        ((int)status).Should().Be(expected);
    }
}
