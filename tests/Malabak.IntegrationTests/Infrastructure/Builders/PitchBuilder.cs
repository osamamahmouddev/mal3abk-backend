using Malabak.Modules.Venues.Domain;

namespace Malabak.IntegrationTests.Infrastructure.Builders;

public sealed class PitchBuilder
{
    private Guid _id = Guid.NewGuid();
    private Guid _venueId = Guid.NewGuid();
    private string _name = "Pitch 1 (5-a-side)";
    private string _turfType = "Artificial 4G";
    private int _capacity = 10;
    private decimal? _lengthMeters = 40.0m;
    private decimal? _widthMeters = 20.0m;
    private readonly List<string> _photos = ["https://assets.malabak.app/pitches/pitch1.jpg"];
    private readonly DateTime _createdAtUtc = DateTime.UtcNow;

    public PitchBuilder WithId(Guid id)
    {
        _id = id;
        return this;
    }

    public PitchBuilder WithVenueId(Guid venueId)
    {
        _venueId = venueId;
        return this;
    }

    public PitchBuilder WithName(string name)
    {
        _name = name;
        return this;
    }

    public PitchBuilder WithTurfType(string turfType)
    {
        _turfType = turfType;
        return this;
    }

    public PitchBuilder WithCapacity(int capacity)
    {
        _capacity = capacity;
        return this;
    }

    public PitchBuilder WithDimensions(decimal lengthMeters, decimal widthMeters)
    {
        _lengthMeters = lengthMeters;
        _widthMeters = widthMeters;
        return this;
    }

    public Pitch Build()
    {
        return Pitch.Create(
            _id,
            _venueId,
            _name,
            _turfType,
            _capacity,
            _lengthMeters,
            _widthMeters,
            _photos,
            _createdAtUtc);
    }
}
