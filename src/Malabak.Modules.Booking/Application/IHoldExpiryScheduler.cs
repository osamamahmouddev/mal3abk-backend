namespace Malabak.Modules.Booking.Application;

public interface IHoldExpiryScheduler
{
    void ScheduleHoldExpiry(Guid bookingId, DateTime holdExpiresAtUtc);
}
