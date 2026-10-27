using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

public sealed class OperatingSchedule : Entity<Guid>
{
    private OperatingSchedule()
    {
    }

    public Guid VenueId { get; private set; }
    public DayOfWeek DayOfWeek { get; private set; }
    public TimeOnly OpenTime { get; private set; }
    public TimeOnly CloseTime { get; private set; }
    public int SlotDurationMinutes { get; private set; } = 60;
    public bool IsActive { get; private set; }

    public bool IsOvernight => CloseTime <= OpenTime;

    public static OperatingSchedule Create(
        Guid id,
        Guid venueId,
        DayOfWeek dayOfWeek,
        TimeOnly openTime,
        TimeOnly closeTime,
        int slotDurationMinutes,
        DateTime createdAtUtc)
    {
        if (slotDurationMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slotDurationMinutes), "Slot duration must be positive.");
        }

        return new OperatingSchedule
        {
            Id = id,
            VenueId = venueId,
            DayOfWeek = dayOfWeek,
            OpenTime = openTime,
            CloseTime = closeTime,
            SlotDurationMinutes = slotDurationMinutes,
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void Update(
        TimeOnly openTime,
        TimeOnly closeTime,
        int slotDurationMinutes,
        bool isActive,
        DateTime nowUtc)
    {
        if (slotDurationMinutes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(slotDurationMinutes), "Slot duration must be positive.");
        }

        OpenTime = openTime;
        CloseTime = closeTime;
        SlotDurationMinutes = slotDurationMinutes;
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
    }
}
