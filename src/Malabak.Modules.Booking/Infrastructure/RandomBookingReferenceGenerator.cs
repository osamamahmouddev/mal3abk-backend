using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;

namespace Malabak.Modules.Booking.Infrastructure;

public sealed class RandomBookingReferenceGenerator : IBookingReferenceGenerator
{
    public string Next() => BookingReference.Generate();
}
