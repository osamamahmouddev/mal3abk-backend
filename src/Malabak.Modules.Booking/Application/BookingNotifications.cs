using System.Globalization;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;

namespace Malabak.Modules.Booking.Application;

/// <summary>
/// Builds every notification the booking module sends, in one place, so the data always matches what the Notifications
/// catalog's templates expect (<c>{venueName}</c>, <c>{date}</c>, <c>{reason}</c> and a <c>bookingId</c> for the deep link). A
/// template placeholder with no value renders empty, so a key that drifts silently produces a blank-gapped message.
/// </summary>
public static class BookingNotifications
{
    /// <summary>The start time as the venue and player see it: Cairo local time.</summary>
    public static string LocalDate(DateTime utc, IClock clock) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), clock.CairoTimeZone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static NotificationRequest Confirmed(Domain.Booking booking, string venueName, IClock clock) =>
        ForPlayer(booking, NotificationTypes.BookingConfirmed, venueName, clock, "booking-confirmed");

    public static NotificationRequest Rejected(Domain.Booking booking, string venueName, string reason, IClock clock) =>
        ForPlayer(booking, NotificationTypes.BookingRejected, venueName, clock, "booking-rejected", reason);

    public static NotificationRequest Expired(Domain.Booking booking, string venueName, IClock clock) =>
        ForPlayer(booking, NotificationTypes.BookingExpired, venueName, clock, "booking-expired");

    public static NotificationRequest Reminder24h(Domain.Booking booking, string venueName, IClock clock) =>
        ForPlayer(booking, NotificationTypes.BookingReminder24h, venueName, clock, "booking-reminder-24h");

    public static NotificationRequest Reminder2h(Domain.Booking booking, string venueName, IClock clock) =>
        ForPlayer(booking, NotificationTypes.BookingReminder2h, venueName, clock, "booking-reminder-2h");

    /// <summary>For one member of the venue's staff; the dedupe key includes them so each person is told once.</summary>
    public static NotificationRequest ProofSubmitted(Domain.Booking booking, string venueName, Guid staffUserId, IClock clock) =>
        new(staffUserId, NotificationTypes.PaymentProofSubmitted, Data(booking, venueName, clock, reason: null), $"proof-submitted:{booking.Id:D}:{staffUserId:D}");

    private static NotificationRequest ForPlayer(Domain.Booking booking, string type, string venueName, IClock clock, string dedupePrefix, string? reason = null) =>
        new(booking.PlayerId, type, Data(booking, venueName, clock, reason), $"{dedupePrefix}:{booking.Id:D}");

    private static Dictionary<string, string> Data(Domain.Booking booking, string venueName, IClock clock, string? reason) => new()
    {
        ["bookingId"] = booking.Id.ToString("D"),
        ["reference"] = booking.BookingReference,
        ["venueId"] = booking.VenueId.ToString("D"),
        ["venueName"] = venueName,
        ["date"] = LocalDate(booking.StartUtc, clock),
        ["reason"] = reason ?? string.Empty,
    };
}
