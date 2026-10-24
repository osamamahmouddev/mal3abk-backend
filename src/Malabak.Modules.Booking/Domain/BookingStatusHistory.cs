namespace Malabak.Modules.Booking.Domain;

public sealed class BookingStatusHistory
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public Guid BookingId { get; init; }
    public BookingStatus FromStatus { get; init; }
    public BookingStatus ToStatus { get; init; }
    public string? Reason { get; init; }
    public string ChangedByUserId { get; init; } = string.Empty;
    public DateTime ChangedAtUtc { get; init; }
}
