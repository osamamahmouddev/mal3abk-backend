namespace Malabak.Modules.Booking.Domain;

public interface IBookingReferenceGenerator
{
    string Generate();
    bool IsValid(string reference);
}
