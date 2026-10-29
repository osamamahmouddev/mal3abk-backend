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

    public PitchCapacity CapacityValue => PitchCapacity.Create(Capacity).Value;

    public SurfaceType Surface => SurfaceType.Create(TurfType).Value;

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

        var surfaceResult = SurfaceType.Create(turfType);
        if (surfaceResult.IsFailure)
        {
            throw new ArgumentException(surfaceResult.Error.Description, nameof(turfType));
        }

        var capacityResult = capacity <= 11
            ? PitchCapacity.Create(capacity)
            : PitchCapacity.FromTotalPlayers(capacity);

        if (capacityResult.IsFailure)
        {
            throw new ArgumentException(capacityResult.Error.Description, nameof(capacity));
        }

        if (lengthMeters.HasValue && lengthMeters.Value <= 0)
        {
            throw new ArgumentException("Length must be greater than zero.", nameof(lengthMeters));
        }

        if (widthMeters.HasValue && widthMeters.Value <= 0)
        {
            throw new ArgumentException("Width must be greater than zero.", nameof(widthMeters));
        }

        var pitch = new Pitch
        {
            Id = id,
            VenueId = venueId,
            Name = name.Trim(),
            TurfType = surfaceResult.Value.Value,
            Capacity = capacityResult.Value.PlayersPerTeam,
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

        var surfaceResult = SurfaceType.Create(turfType);
        if (surfaceResult.IsFailure)
        {
            throw new ArgumentException(surfaceResult.Error.Description, nameof(turfType));
        }

        var capacityResult = capacity <= 11
            ? PitchCapacity.Create(capacity)
            : PitchCapacity.FromTotalPlayers(capacity);

        if (capacityResult.IsFailure)
        {
            throw new ArgumentException(capacityResult.Error.Description, nameof(capacity));
        }

        if (lengthMeters.HasValue && lengthMeters.Value <= 0)
        {
            throw new ArgumentException("Length must be greater than zero.", nameof(lengthMeters));
        }

        if (widthMeters.HasValue && widthMeters.Value <= 0)
        {
            throw new ArgumentException("Width must be greater than zero.", nameof(widthMeters));
        }

        Name = name.Trim();
        TurfType = surfaceResult.Value.Value;
        Capacity = capacityResult.Value.PlayersPerTeam;
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

    public const int MaxPhotos = 20;

    /// <summary>Adds a gallery photo (a URL produced by the media pipeline). Returns false when full or already present.</summary>
    public bool AddPhoto(string url, DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (_photos.Count >= MaxPhotos || _photos.Contains(url, StringComparer.Ordinal))
        {
            return false;
        }

        _photos.Add(url);
        UpdatedAtUtc = nowUtc;
        return true;
    }

    public bool RemovePhoto(string url, DateTime nowUtc)
    {
        var removed = _photos.Remove(url);
        if (removed)
        {
            UpdatedAtUtc = nowUtc;
        }

        return removed;
    }

    public void MarkDeleted(DateTime nowUtc)
    {
        IsDeleted = true;
        UpdatedAtUtc = nowUtc;
    }
}
