using Malabak.Modules.Venues.Domain;

namespace Malabak.IntegrationTests.Infrastructure.Builders;

public sealed class VenueBuilder
{
    private Guid _id = Guid.NewGuid();
    private string _name = "Al-Ahly Club Venue";
    private string _slug = "al-ahly-club-venue";
    private readonly string? _description = "Premier sporting venue in Nasr City, Cairo";
    private Guid _ownerId = Guid.NewGuid();
    private string _address = "Nasr City, Cairo";
    private string _city = "Cairo";
    private string _area = "Nasr City";
    private double? _latitude = 30.0561;
    private double? _longitude = 31.3301;
    private readonly string _phoneNumber = "+201000000001";
    private List<string> _facilities = ["Floodlights", "Parking", "Locker Rooms", "Cafeteria"];
    private readonly List<string> _photos = ["https://assets.malabak.app/venues/ahly_main.jpg"];
    private readonly DateTime _createdAtUtc = DateTime.UtcNow;

    public VenueBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    public VenueBuilder WithName(string name, string? slug = null)
    {
        _name = name;
        _slug = slug ?? name.ToLowerInvariant().Replace(' ', '-');
        return this;
    }

    public VenueBuilder WithOwnerId(Guid ownerId)
    {
        _ownerId = ownerId;
        return this;
    }

    public VenueBuilder WithAddress(string address, string city = "Cairo", string area = "Nasr City")
    {
        _address = address;
        _city = city;
        _area = area;
        return this;
    }

    public VenueBuilder WithCoordinates(double latitude, double longitude)
    {
        _latitude = latitude;
        _longitude = longitude;
        return this;
    }

    public VenueBuilder WithFacilities(params string[] facilities)
    {
        _facilities = [.. facilities];
        return this;
    }

    public Venue Build()
    {
        return Venue.Create(
            _id,
            _name,
            _slug,
            _description,
            _ownerId,
            _address,
            _city,
            _area,
            _latitude,
            _longitude,
            _phoneNumber,
            _facilities,
            _photos,
            _createdAtUtc);
    }
}
