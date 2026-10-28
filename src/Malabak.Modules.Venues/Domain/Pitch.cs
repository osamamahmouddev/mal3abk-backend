using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

public sealed class Pitch : Entity<Guid>
{
    private readonly List<string> _photos = [];

    private Pitch()
    {
    }

    public Guid VenueId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string TurfType { get; private set; } = string.Empty;
    public int Capacity { get; private set; }
    public decimal? LengthMeters { get; private set; }
    public decimal? WidthMeters { get; private set; }
    public bool IsActive { get; private set; }

    public IReadOnlyCollection<string> Photos => _photos.AsReadOnly();

    public static Pitch Create(
        Guid id,
        Guid venueId,
        string name,
        string turfType,
        int capacity,
        decimal? lengthMeters,
        decimal? widthMeters,
        IEnumerable<string>? photos,
        DateTime createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(turfType);

        var pitch = new Pitch
        {
            Id = id,
            VenueId = venueId,
            Name = name.Trim(),
            TurfType = turfType.Trim(),
            Capacity = capacity,
            LengthMeters = lengthMeters,
            WidthMeters = widthMeters,
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };

        if (photos != null)
        {
            pitch._photos.AddRange(photos);
        }

        return pitch;
    }

    public void Update(
        string name,
        string turfType,
        int capacity,
        decimal? lengthMeters,
        decimal? widthMeters,
        IEnumerable<string>? photos,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(turfType);

        Name = name.Trim();
        TurfType = turfType.Trim();
        Capacity = capacity;
        LengthMeters = lengthMeters;
        WidthMeters = widthMeters;

        _photos.Clear();
        if (photos != null)
        {
            _photos.AddRange(photos);
        }

        UpdatedAtUtc = nowUtc;
    }

    public void SetActive(bool isActive, DateTime nowUtc)
    {
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
    }
}
