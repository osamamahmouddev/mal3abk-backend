namespace Malabak.Modules.Booking.Application.Abstractions;

/// <summary>Produces public booking references. A seam so tests can force collisions.</summary>
public interface IBookingReferenceGenerator
{
    string Next();
}
