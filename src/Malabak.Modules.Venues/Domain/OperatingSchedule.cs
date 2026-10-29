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

    public int TotalDurationMinutes
    {
        get
        {
            if (IsOvernight)
            {
                var beforeMidnight = TimeSpan.FromHours(24) - OpenTime.ToTimeSpan();
                var afterMidnight = CloseTime.ToTimeSpan();
                return (int)(beforeMidnight + afterMidnight).TotalMinutes;
            }

            return (int)(CloseTime - OpenTime).TotalMinutes;
        }
    }

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

    public IReadOnlyList<ScheduleSlot> GenerateSlots(DateOnly businessDate, TimeZoneInfo cairoTimeZone)
    {
        if (!IsActive || SlotDurationMinutes <= 0)
        {
            return Array.Empty<ScheduleSlot>();
        }

        var startLocal = businessDate.ToDateTime(OpenTime);
        var endLocal = IsOvernight
            ? businessDate.AddDays(1).ToDateTime(CloseTime)
            : businessDate.ToDateTime(CloseTime);

        if (endLocal <= startLocal)
        {
            return Array.Empty<ScheduleSlot>();
        }

        var slots = new List<ScheduleSlot>();
        var currentStart = startLocal;
        var slotIndex = 0;

        while (currentStart.AddMinutes(SlotDurationMinutes) <= endLocal)
        {
            var currentEnd = currentStart.AddMinutes(SlotDurationMinutes);
            var startUtc = ConvertToUtc(currentStart, cairoTimeZone);
            var endUtc = ConvertToUtc(currentEnd, cairoTimeZone);
            var isSlotOvernight = DateOnly.FromDateTime(currentStart) > businessDate;

            slots.Add(new ScheduleSlot(
                businessDate,
                slotIndex++,
                currentStart,
                currentEnd,
                startUtc,
                endUtc,
                isSlotOvernight));

            currentStart = currentEnd;
        }

        return slots.AsReadOnly();
    }

    public static DateTime ConvertToUtc(DateTime localDateTime, TimeZoneInfo timeZone)
    {
        var unspecified = DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified);

        if (timeZone.IsInvalidTime(unspecified))
        {
            var gap = TimeSpan.FromHours(1);
            var adjusted = unspecified.Add(gap);
            return TimeZoneInfo.ConvertTimeToUtc(adjusted, timeZone);
        }

        if (timeZone.IsAmbiguousTime(unspecified))
        {
            var offsets = timeZone.GetAmbiguousTimeOffsets(unspecified);
            var offset = offsets[0];
            var dto = new DateTimeOffset(unspecified, offset);
            return dto.UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(unspecified, timeZone);
    }
}
