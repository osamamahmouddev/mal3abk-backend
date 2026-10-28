namespace Malabak.SharedKernel;

public sealed record TimeSlotRange : IComparable<TimeSlotRange>
{
    private static readonly TimeSpan _maxDuration = TimeSpan.FromHours(24);

    public DateTime StartUtc { get; init; }
    public DateTime EndUtc { get; init; }

    public TimeSpan Duration => EndUtc - StartUtc;

    public TimeSlotRange(DateTime startUtc, DateTime endUtc)
    {
        var result = Create(startUtc, endUtc);
        if (result.IsFailure)
        {
            throw new ArgumentException(result.Error.Description, nameof(endUtc));
        }

        StartUtc = result.Value.StartUtc;
        EndUtc = result.Value.EndUtc;
    }

    private TimeSlotRange()
    {
    }

    public static Result<TimeSlotRange> Create(DateTime startUtc, DateTime endUtc)
    {
        var normalizedStart = NormalizeToUtc(startUtc);
        var normalizedEnd = NormalizeToUtc(endUtc);

        if (normalizedStart >= normalizedEnd)
        {
            return Result.Failure<TimeSlotRange>(TimeSlotRangeErrors.InvalidRange);
        }

        var duration = normalizedEnd - normalizedStart;
        if (duration <= TimeSpan.Zero)
        {
            return Result.Failure<TimeSlotRange>(TimeSlotRangeErrors.ZeroOrNegativeDuration);
        }

        if (duration > _maxDuration)
        {
            return Result.Failure<TimeSlotRange>(TimeSlotRangeErrors.ExceedsMaxDuration);
        }

        return Result.Success(new TimeSlotRange
        {
            StartUtc = normalizedStart,
            EndUtc = normalizedEnd
        });
    }

    public static Result<TimeSlotRange> Create(DateTime startUtc, TimeSpan duration)
    {
        if (duration <= TimeSpan.Zero)
        {
            return Result.Failure<TimeSlotRange>(TimeSlotRangeErrors.ZeroOrNegativeDuration);
        }

        return Create(startUtc, startUtc.Add(duration));
    }

    public static Result<TimeSlotRange> FromDateAndTimes(
        DateOnly date,
        TimeOnly startTime,
        TimeOnly endTime,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(timeZone);

        var startLocal = date.ToDateTime(startTime, DateTimeKind.Unspecified);
        var endDate = endTime <= startTime ? date.AddDays(1) : date;
        var endLocal = endDate.ToDateTime(endTime, DateTimeKind.Unspecified);

        var startUtc = TimeZoneInfo.ConvertTimeToUtc(startLocal, timeZone);
        var endUtc = TimeZoneInfo.ConvertTimeToUtc(endLocal, timeZone);

        return Create(startUtc, endUtc);
    }

    public static TimeSlotRange From(DateTime startUtc, DateTime endUtc) => new(startUtc, endUtc);

    public bool Overlaps(TimeSlotRange other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return StartUtc < other.EndUtc && other.StartUtc < EndUtc;
    }

    public bool Touches(TimeSlotRange other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return EndUtc == other.StartUtc || StartUtc == other.EndUtc;
    }

    public bool Contains(DateTime timestampUtc)
    {
        var normalized = NormalizeToUtc(timestampUtc);
        return normalized >= StartUtc && normalized < EndUtc;
    }

    public bool Contains(TimeSlotRange other)
    {
        ArgumentNullException.ThrowIfNull(other);

        return StartUtc <= other.StartUtc && other.EndUtc <= EndUtc;
    }

    public Result<TimeSlotRange> MergeWith(TimeSlotRange other)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (!Overlaps(other) && !Touches(other))
        {
            return Result.Failure<TimeSlotRange>(
                Error.Conflict("TimeSlotRange.CannotMerge", "Cannot merge non-adjacent and non-overlapping slot ranges."));
        }

        var minStart = StartUtc < other.StartUtc ? StartUtc : other.StartUtc;
        var maxEnd = EndUtc > other.EndUtc ? EndUtc : other.EndUtc;

        return Create(minStart, maxEnd);
    }

    public int CompareTo(TimeSlotRange? other)
    {
        if (other is null)
        {
            return 1;
        }

        var startComparison = StartUtc.CompareTo(other.StartUtc);
        return startComparison != 0 ? startComparison : EndUtc.CompareTo(other.EndUtc);
    }

    public override string ToString() => $"{StartUtc:yyyy-MM-ddTHH:mm:ssZ}/{EndUtc:yyyy-MM-ddTHH:mm:ssZ}";

    private static DateTime NormalizeToUtc(DateTime dateTime)
    {
        return dateTime.Kind switch
        {
            DateTimeKind.Utc => dateTime,
            DateTimeKind.Local => dateTime.ToUniversalTime(),
            _ => DateTime.SpecifyKind(dateTime, DateTimeKind.Utc)
        };
    }
}
