using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

public sealed class BlockedSlot : Entity<Guid>
{
    private BlockedSlot()
    {
    }

    public Guid VenueId { get; private set; }
    public Guid PitchId { get; private set; }
    public DateTime StartUtc { get; private set; }
    public DateTime EndUtc { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public Guid BlockedByUserId { get; private set; }

    public static BlockedSlot Create(
        Guid id,
        Guid venueId,
        Guid pitchId,
        DateTime startUtc,
        DateTime endUtc,
        string reason,
        Guid blockedByUserId,
        DateTime createdAtUtc)
    {
        if (endUtc <= startUtc)
        {
            throw new ArgumentException("End time must be after start time.", nameof(endUtc));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new BlockedSlot
        {
            Id = id,
            VenueId = venueId,
            PitchId = pitchId,
            StartUtc = startUtc,
            EndUtc = endUtc,
            Reason = reason.Trim(),
            BlockedByUserId = blockedByUserId,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void MarkDeleted(DateTime nowUtc)
    {
        IsDeleted = true;
        UpdatedAtUtc = nowUtc;
    }
}
