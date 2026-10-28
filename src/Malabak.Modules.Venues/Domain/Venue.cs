using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

public sealed class Venue : Entity<Guid>
{
    private readonly List<string> _facilities = [];
    private readonly List<string> _photos = [];
    private readonly List<Pitch> _pitches = [];
    private readonly List<OperatingSchedule> _operatingSchedules = [];
    private readonly List<PriceRule> _priceRules = [];
    private readonly List<BlockedSlot> _blockedSlots = [];
    private readonly List<VenuePolicy> _policies = [];
    private readonly List<VenuePaymentMethod> _paymentMethods = [];

    private Venue()
    {
    }

    public string Name { get; private set; } = string.Empty;
    public string Slug { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Address { get; private set; } = string.Empty;
    public string City { get; private set; } = string.Empty;
    public string Area { get; private set; } = string.Empty;
    public double? Latitude { get; private set; }
    public double? Longitude { get; private set; }
    public string PhoneNumber { get; private set; } = string.Empty;
    public bool IsApproved { get; private set; }
    public bool IsPublished { get; private set; }

    public IReadOnlyCollection<string> Facilities => _facilities.AsReadOnly();
    public IReadOnlyCollection<string> Photos => _photos.AsReadOnly();
    public IReadOnlyCollection<Pitch> Pitches => _pitches.AsReadOnly();
    public IReadOnlyCollection<OperatingSchedule> OperatingSchedules => _operatingSchedules.AsReadOnly();
    public IReadOnlyCollection<PriceRule> PriceRules => _priceRules.AsReadOnly();
    public IReadOnlyCollection<BlockedSlot> BlockedSlots => _blockedSlots.AsReadOnly();
    public IReadOnlyCollection<VenuePolicy> Policies => _policies.AsReadOnly();
    public IReadOnlyCollection<VenuePaymentMethod> PaymentMethods => _paymentMethods.AsReadOnly();

    public static Venue Create(
        Guid id,
        string name,
        string slug,
        string? description,
        Guid ownerId,
        string address,
        string city,
        string area,
        double? latitude,
        double? longitude,
        string phoneNumber,
        IEnumerable<string>? facilities,
        IEnumerable<string>? photos,
        DateTime createdAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        var venue = new Venue
        {
            Id = id,
            Name = name.Trim(),
            Slug = slug.Trim().ToLowerInvariant(),
            Description = description?.Trim(),
            OwnerId = ownerId,
            Address = address.Trim(),
            City = city.Trim(),
            Area = area.Trim(),
            Latitude = latitude,
            Longitude = longitude,
            PhoneNumber = phoneNumber.Trim(),
            IsApproved = false,
            IsPublished = false,
            CreatedAtUtc = createdAtUtc
        };

        if (facilities != null)
        {
            venue._facilities.AddRange(facilities);
        }

        if (photos != null)
        {
            venue._photos.AddRange(photos);
        }

        return venue;
    }

    public void UpdateDetails(
        string name,
        string? description,
        string address,
        string city,
        string area,
        double? latitude,
        double? longitude,
        string phoneNumber,
        IEnumerable<string>? facilities,
        IEnumerable<string>? photos,
        DateTime nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);
        ArgumentException.ThrowIfNullOrWhiteSpace(city);
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        ArgumentException.ThrowIfNullOrWhiteSpace(phoneNumber);

        Name = name.Trim();
        Description = description?.Trim();
        Address = address.Trim();
        City = city.Trim();
        Area = area.Trim();
        Latitude = latitude;
        Longitude = longitude;
        PhoneNumber = phoneNumber.Trim();

        _facilities.Clear();
        if (facilities != null)
        {
            _facilities.AddRange(facilities);
        }

        _photos.Clear();
        if (photos != null)
        {
            _photos.AddRange(photos);
        }

        UpdatedAtUtc = nowUtc;
    }

    public void SetApproved(bool isApproved, DateTime nowUtc)
    {
        IsApproved = isApproved;
        if (!isApproved)
        {
            IsPublished = false;
        }
        UpdatedAtUtc = nowUtc;
    }

    public void SetPublished(bool isPublished, DateTime nowUtc)
    {
        if (isPublished && !IsApproved)
        {
            throw new InvalidOperationException("Cannot publish a venue that is not approved.");
        }

        IsPublished = isPublished;
        UpdatedAtUtc = nowUtc;
    }
}
