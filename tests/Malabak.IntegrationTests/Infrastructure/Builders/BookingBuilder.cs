using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;

namespace Malabak.IntegrationTests.Infrastructure.Builders;

public sealed class BookingBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _bookingReference = $"BK-{Guid.NewGuid():N}"[..12].ToUpperInvariant();
    private Guid _pitchId = Guid.NewGuid();
    private Guid _playerId = Guid.NewGuid();
    private DateTime _startUtc = DateTime.UtcNow.AddHours(2);
    private DateTime _endUtc = DateTime.UtcNow.AddHours(3);
    private Money _totalPrice = Money.Create(500.00m, "EGP").Value;
    private Money _depositAmount = Money.Create(150.00m, "EGP").Value;
    private readonly DateTime _holdExpiresAtUtc = DateTime.UtcNow.AddMinutes(15);
    private readonly DateTime _nowUtc = DateTime.UtcNow;
    private readonly string _actorUserId = Guid.NewGuid().ToString();

    public BookingBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    public BookingBuilder WithBookingReference(string bookingReference)
    {
        _bookingReference = bookingReference;
        return this;
    }

    public BookingBuilder ForPitch(Guid pitchId)
    {
        _pitchId = pitchId;
        return this;
    }

    public BookingBuilder ForPlayer(Guid playerId)
    {
        _playerId = playerId;
        return this;
    }

    public BookingBuilder WithTimeSlot(DateTime startUtc, DateTime endUtc)
    {
        _startUtc = startUtc;
        _endUtc = endUtc;
        return this;
    }

    public BookingBuilder WithPrice(decimal totalAmount, decimal depositAmount, string currency = "EGP")
    {
        _totalPrice = Money.Create(totalAmount, currency).Value;
        _depositAmount = Money.Create(depositAmount, currency).Value;
        return this;
    }

    public Booking Build()
    {
        var result = Booking.Create(
            _id,
            _bookingReference,
            _pitchId,
            _playerId,
            _startUtc,
            _endUtc,
            _totalPrice,
            _depositAmount,
            _holdExpiresAtUtc,
            _nowUtc,
            _actorUserId);

        if (result.IsFailure)
        {
            throw new InvalidOperationException($"Failed to build Booking: {result.Error.Code} - {result.Error.Description}");
        }

        return result.Value;
    }
}
