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
    public VenueApprovalStatus ApprovalStatus { get; private set; }

    /// <summary>Denormalized from <see cref="ApprovalStatus"/> for the search index (approved AND published).</summary>
    public bool IsApproved { get; private set; }
    public bool IsPublished { get; private set; }

    /// <summary>Visible to players only when an admin approved it AND the owner chose to publish it.</summary>
    public bool IsSearchable => ApprovalStatus == VenueApprovalStatus.Approved && IsPublished && !IsDeleted;

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

        ValidateCoordinates(latitude, longitude);

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

        ValidateCoordinates(latitude, longitude);

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

    /// <summary>Owner sends the venue for review (first time, after a rejection, or answering an info request).</summary>
    public Result<VenueApprovalEvent> SubmitForApproval(Guid ownerId, string? message, DateTime nowUtc)
    {
        if (ApprovalStatus is not (VenueApprovalStatus.Draft or VenueApprovalStatus.NeedsInfo or VenueApprovalStatus.Rejected))
        {
            return Result.Failure<VenueApprovalEvent>(VenueApprovalErrors.InvalidTransition(ApprovalStatus, "submit"));
        }

        return Transition(VenueApprovalAction.Submitted, VenueApprovalStatus.Submitted, ownerId, message, nowUtc);
    }

    public Result<VenueApprovalEvent> Approve(Guid adminId, string? note, DateTime nowUtc) =>
        DecideOnSubmission(VenueApprovalAction.Approved, VenueApprovalStatus.Approved, adminId, note, requireMessage: false, nowUtc);

    public Result<VenueApprovalEvent> Reject(Guid adminId, string reason, DateTime nowUtc) =>
        DecideOnSubmission(VenueApprovalAction.Rejected, VenueApprovalStatus.Rejected, adminId, reason, requireMessage: true, nowUtc);

    public Result<VenueApprovalEvent> RequestMoreInfo(Guid adminId, string message, DateTime nowUtc) =>
        DecideOnSubmission(VenueApprovalAction.InfoRequested, VenueApprovalStatus.NeedsInfo, adminId, message, requireMessage: true, nowUtc);

    private Result<VenueApprovalEvent> DecideOnSubmission(
        VenueApprovalAction action, VenueApprovalStatus to, Guid adminId, string? message, bool requireMessage, DateTime nowUtc)
    {
        if (ApprovalStatus != VenueApprovalStatus.Submitted)
        {
            return Result.Failure<VenueApprovalEvent>(VenueApprovalErrors.InvalidTransition(ApprovalStatus, action.ToString()));
        }

        if (requireMessage && string.IsNullOrWhiteSpace(message))
        {
            return Result.Failure<VenueApprovalEvent>(VenueApprovalErrors.MessageRequired);
        }

        return Transition(action, to, adminId, message, nowUtc);
    }

    private Result<VenueApprovalEvent> Transition(
        VenueApprovalAction action, VenueApprovalStatus to, Guid actorId, string? message, DateTime nowUtc)
    {
        if (message?.Trim().Length > VenueApprovalEvent.MaxMessageLength)
        {
            return Result.Failure<VenueApprovalEvent>(VenueApprovalErrors.MessageTooLong);
        }

        var approvalEvent = VenueApprovalEvent.Create(Id, action, ApprovalStatus, to, actorId, message, nowUtc);
        ApprovalStatus = to;
        IsApproved = to == VenueApprovalStatus.Approved;

        // Losing approval always hides the venue; the owner must re-publish after re-approval.
        if (!IsApproved)
        {
            IsPublished = false;
        }

        UpdatedAtUtc = nowUtc;
        return Result.Success(approvalEvent);
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

    public void AddPitch(Pitch pitch)
    {
        ArgumentNullException.ThrowIfNull(pitch);
        _pitches.Add(pitch);
    }

    public void RemovePitch(Guid pitchId)
    {
        _pitches.RemoveAll(p => p.Id == pitchId);
    }

    public void SetOperatingSchedules(IEnumerable<OperatingSchedule> schedules, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(schedules);

        _operatingSchedules.Clear();
        _operatingSchedules.AddRange(schedules);
        UpdatedAtUtc = nowUtc;
    }

    public void MarkDeleted(DateTime nowUtc)
    {
        IsDeleted = true;
        UpdatedAtUtc = nowUtc;
    }

    private static void ValidateCoordinates(double? latitude, double? longitude)
    {
        if (latitude.HasValue && (latitude.Value < -90 || latitude.Value > 90))
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "Latitude must be between -90 and 90 degrees.");
        }

        if (longitude.HasValue && (longitude.Value < -180 || longitude.Value > 180))
        {
            throw new ArgumentOutOfRangeException(nameof(longitude), "Longitude must be between -180 and 180 degrees.");
        }
    }
}
