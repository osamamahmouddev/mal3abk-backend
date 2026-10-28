using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

public sealed class PriceRule : Entity<Guid>
{
    private PriceRule()
    {
    }

    public Guid VenueId { get; private set; }
    public Guid? PitchId { get; private set; }
    public DayOfWeek? DayOfWeek { get; private set; }
    public TimeOnly? StartTime { get; private set; }
    public TimeOnly? EndTime { get; private set; }
    public Money Price { get; private set; } = null!;
    public bool IsActive { get; private set; }

    public static PriceRule Create(
        Guid id,
        Guid venueId,
        Guid? pitchId,
        DayOfWeek? dayOfWeek,
        TimeOnly? startTime,
        TimeOnly? endTime,
        Money price,
        DateTime createdAtUtc)
    {
        ArgumentNullException.ThrowIfNull(price);

        return new PriceRule
        {
            Id = id,
            VenueId = venueId,
            PitchId = pitchId,
            DayOfWeek = dayOfWeek,
            StartTime = startTime,
            EndTime = endTime,
            Price = price,
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };
    }

    public void Update(
        Money price,
        DayOfWeek? dayOfWeek,
        TimeOnly? startTime,
        TimeOnly? endTime,
        bool isActive,
        DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(price);

        Price = price;
        DayOfWeek = dayOfWeek;
        StartTime = startTime;
        EndTime = endTime;
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
    }
}
