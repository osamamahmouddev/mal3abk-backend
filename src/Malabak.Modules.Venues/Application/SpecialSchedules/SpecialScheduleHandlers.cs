using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Reference;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.SpecialSchedules;

public sealed record WeeklyHoursInput(DayOfWeek DayOfWeek, TimeOnly OpenTime, TimeOnly CloseTime, bool IsClosed);

/// <summary>
/// Result of a schedule change. When future bookings would fall outside the new hours and the caller did not
/// confirm, nothing is saved and <see cref="Applied"/> is false so the client can show the list and ask again.
/// </summary>
public sealed record ScheduleChangeOutcome(bool Applied, Guid? Id, IReadOnlyList<UpcomingBookingRef> AffectedBookings);

public sealed record AddScheduleOverrideCommand(
    Guid VenueId, Guid UserId, Guid? PitchId, DateOnly StartDate, DateOnly EndDate, ScheduleOverrideKind Kind,
    TimeOnly? FromTime, TimeOnly? ToTime, string Reason, bool Confirm) : ICommand<Result<ScheduleChangeOutcome>>;

public sealed record RemoveScheduleOverrideCommand(Guid VenueId, Guid UserId, Guid ExceptionId, bool Confirm) : ICommand<Result<ScheduleChangeOutcome>>;

public sealed record ApplyHolidayClosuresCommand(Guid VenueId, Guid UserId, int Year, Guid? PitchId, bool Confirm) : ICommand<Result<ScheduleChangeOutcome>>;

/// <summary>Replaces the pitch's recurring weekly hours; an empty list reverts the pitch to the venue hours.</summary>
public sealed record SetPitchWeeklyHoursCommand(Guid VenueId, Guid UserId, Guid PitchId, IReadOnlyList<WeeklyHoursInput> Days, bool Confirm)
    : ICommand<Result<ScheduleChangeOutcome>>;

public sealed record SetBookingRulesCommand(
    Guid VenueId, Guid UserId, Guid? PitchId, int MinDurationMinutes, int MaxDurationMinutes, int IncrementMinutes, int MinNoticeMinutes, int MaxAdvanceDays)
    : ICommand<Result<BookingRulesDto>>;

public sealed record BookingRulesDto(Guid? PitchId, int MinDurationMinutes, int MaxDurationMinutes, int IncrementMinutes, int MinNoticeMinutes, int MaxAdvanceDays);

public sealed record ScheduleOverrideDto(
    Guid Id, Guid? PitchId, DateOnly StartDate, DateOnly EndDate, string Kind, TimeOnly? FromTime, TimeOnly? ToTime, string Reason);

public sealed record PitchWeeklyHoursDto(Guid PitchId, DayOfWeek DayOfWeek, TimeOnly OpenTime, TimeOnly CloseTime, bool IsClosed);

public sealed record SpecialSchedulesDto(
    IReadOnlyList<ScheduleOverrideDto> Exceptions, IReadOnlyList<PitchWeeklyHoursDto> PitchHours, IReadOnlyList<BookingRulesDto> Rules);

public sealed record AvailableStartDto(DateTime StartUtc, DateTime StartCairo);

public sealed record PitchAvailabilityDto(Guid PitchId, DateOnly BusinessDate, IReadOnlyList<UtcWindow> OpenWindows, IReadOnlyList<AvailableStartDto> StartTimes, BookingRulesDto Rules);

/// <summary>
/// Closures, holidays, special hours, breaks, per-pitch weekly hours and booking rules (TASK-220). Changes that
/// reduce open hours list the future bookings they would leave outside the schedule and need explicit confirmation.
/// </summary>
public sealed class SpecialScheduleHandlers :
    ICommandHandler<AddScheduleOverrideCommand, Result<ScheduleChangeOutcome>>,
    ICommandHandler<RemoveScheduleOverrideCommand, Result<ScheduleChangeOutcome>>,
    ICommandHandler<ApplyHolidayClosuresCommand, Result<ScheduleChangeOutcome>>,
    ICommandHandler<SetPitchWeeklyHoursCommand, Result<ScheduleChangeOutcome>>,
    ICommandHandler<SetBookingRulesCommand, Result<BookingRulesDto>>
{
    /// <summary>How far ahead recurring changes are checked against existing bookings.</summary>
    public const int RecurringImpactDays = 365;

    private readonly VenuesDbContext _dbContext;
    private readonly IVenueAccessService _access;
    private readonly IUpcomingBookingsReader _bookings;
    private readonly IClock _clock;

    public SpecialScheduleHandlers(VenuesDbContext dbContext, IVenueAccessService access, IUpcomingBookingsReader bookings, IClock clock)
    {
        _dbContext = dbContext;
        _access = access;
        _bookings = bookings;
        _clock = clock;
    }

    public async Task<Result<ScheduleChangeOutcome>> HandleAsync(AddScheduleOverrideCommand command, CancellationToken cancellationToken = default)
    {
        var denied = await AuthorizeAsync(command.UserId, command.VenueId, command.PitchId, Permissions.SchedulesManage, cancellationToken);
        if (denied is not null)
        {
            return Result.Failure<ScheduleChangeOutcome>(denied);
        }

        var created = ScheduleOverride.Create(
            command.VenueId, command.PitchId, command.StartDate, command.EndDate, command.Kind, command.FromTime, command.ToTime,
            command.Reason, command.UserId, _clock.UtcNow);
        if (created.IsFailure)
        {
            return Result.Failure<ScheduleChangeOutcome>(created.Error);
        }

        var exception = created.Value;
        var (from, to) = DateRangeUtc(exception.StartDate, exception.EndDate);
        return await ApplyAsync(
            command.VenueId, command.Confirm, from, to, state => state with { Exceptions = [.. state.Exceptions, exception] },
            () => _dbContext.ScheduleOverrides.Add(exception), exception.Id, cancellationToken);
    }

    public async Task<Result<ScheduleChangeOutcome>> HandleAsync(RemoveScheduleOverrideCommand command, CancellationToken cancellationToken = default)
    {
        var denied = await AuthorizeAsync(command.UserId, command.VenueId, null, Permissions.SchedulesManage, cancellationToken);
        if (denied is not null)
        {
            return Result.Failure<ScheduleChangeOutcome>(denied);
        }

        var exception = await _dbContext.ScheduleOverrides
            .FirstOrDefaultAsync(e => e.Id == command.ExceptionId && e.VenueId == command.VenueId && !e.IsDeleted, cancellationToken);
        if (exception is null)
        {
            return Result.Failure<ScheduleChangeOutcome>(SpecialScheduleErrors.OverrideNotFound);
        }

        // Removing special hours can shrink the day, so it gets the same impact check.
        var (from, to) = DateRangeUtc(exception.StartDate, exception.EndDate);
        return await ApplyAsync(
            command.VenueId, command.Confirm, from, to, state => state with { Exceptions = state.Exceptions.Where(e => e.Id != exception.Id).ToList() },
            () => exception.MarkDeleted(_clock.UtcNow), exception.Id, cancellationToken);
    }

    public async Task<Result<ScheduleChangeOutcome>> HandleAsync(ApplyHolidayClosuresCommand command, CancellationToken cancellationToken = default)
    {
        var denied = await AuthorizeAsync(command.UserId, command.VenueId, command.PitchId, Permissions.SchedulesManage, cancellationToken);
        if (denied is not null)
        {
            return Result.Failure<ScheduleChangeOutcome>(denied);
        }

        var today = _clock.CairoToday;
        if (command.Year < today.Year || command.Year > today.Year + 1)
        {
            return Result.Failure<ScheduleChangeOutcome>(SpecialScheduleErrors.InvalidDates);
        }

        var alreadyClosed = await _dbContext.ScheduleOverrides
            .Where(e => e.VenueId == command.VenueId && e.PitchId == command.PitchId && e.Kind == ScheduleOverrideKind.Closure && !e.IsDeleted)
            .Select(e => new { e.StartDate, e.EndDate })
            .ToListAsync(cancellationToken);
        var closures = EgyptHolidayCalendar.ForYear(command.Year)
            .Where(h => h.Date >= today && !alreadyClosed.Exists(c => h.Date >= c.StartDate && h.Date <= c.EndDate))
            .Select(h => ScheduleOverride.Create(
                command.VenueId, command.PitchId, h.Date, h.Date, ScheduleOverrideKind.Closure, null, null, h.NameEn, command.UserId, _clock.UtcNow).Value)
            .ToList();
        if (closures.Count == 0)
        {
            return Result.Success(new ScheduleChangeOutcome(true, null, []));
        }

        var (from, to) = DateRangeUtc(closures.Min(c => c.StartDate), closures.Max(c => c.EndDate));
        return await ApplyAsync(
            command.VenueId, command.Confirm, from, to, state => state with { Exceptions = [.. state.Exceptions, .. closures] },
            () => _dbContext.ScheduleOverrides.AddRange(closures), null, cancellationToken);
    }

    public async Task<Result<ScheduleChangeOutcome>> HandleAsync(SetPitchWeeklyHoursCommand command, CancellationToken cancellationToken = default)
    {
        var denied = await AuthorizeAsync(command.UserId, command.VenueId, command.PitchId, Permissions.SchedulesManage, cancellationToken);
        if (denied is not null)
        {
            return Result.Failure<ScheduleChangeOutcome>(denied);
        }

        if (command.Days.Select(d => d.DayOfWeek).Distinct().Count() != command.Days.Count || command.Days.Any(d => !Enum.IsDefined(d.DayOfWeek)))
        {
            return Result.Failure<ScheduleChangeOutcome>(SpecialScheduleErrors.InvalidWeeklyHours);
        }

        var nowUtc = _clock.UtcNow;
        var existing = await _dbContext.PitchWeeklyHours.Where(h => h.PitchId == command.PitchId && !h.IsDeleted).ToListAsync(cancellationToken);
        var replacement = command.Days
            .Select(d => PitchWeeklyHours.Create(command.VenueId, command.PitchId, d.DayOfWeek, d.OpenTime, d.CloseTime, d.IsClosed, nowUtc))
            .ToList();

        return await ApplyAsync(
            command.VenueId, command.Confirm, nowUtc, nowUtc.AddDays(RecurringImpactDays),
            state => state.PitchId == command.PitchId ? state with { PitchWeek = replacement } : state,
            () =>
            {
                _dbContext.PitchWeeklyHours.RemoveRange(existing);
                _dbContext.PitchWeeklyHours.AddRange(replacement);
            },
            command.PitchId, cancellationToken);
    }

    public async Task<Result<BookingRulesDto>> HandleAsync(SetBookingRulesCommand command, CancellationToken cancellationToken = default)
    {
        var denied = await AuthorizeAsync(command.UserId, command.VenueId, command.PitchId, Permissions.SchedulesManage, cancellationToken);
        if (denied is not null)
        {
            return Result.Failure<BookingRulesDto>(denied);
        }

        var nowUtc = _clock.UtcNow;
        var rules = await _dbContext.BookingRules.FirstOrDefaultAsync(r => r.VenueId == command.VenueId && r.PitchId == command.PitchId, cancellationToken);
        if (rules is null)
        {
            var created = BookingRules.Create(command.VenueId, command.PitchId, command.MinDurationMinutes, command.MaxDurationMinutes,
                command.IncrementMinutes, command.MinNoticeMinutes, command.MaxAdvanceDays, nowUtc);
            if (created.IsFailure)
            {
                return Result.Failure<BookingRulesDto>(created.Error);
            }

            rules = created.Value;
            _dbContext.BookingRules.Add(rules);
        }
        else
        {
            var updated = rules.Update(command.MinDurationMinutes, command.MaxDurationMinutes, command.IncrementMinutes,
                command.MinNoticeMinutes, command.MaxAdvanceDays, nowUtc);
            if (updated.IsFailure)
            {
                return Result.Failure<BookingRulesDto>(updated.Error);
            }
        }

        // Rules only apply to new bookings, so there is nothing to warn about.
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(SpecialScheduleQueries.ToDto(rules));
    }

    private async Task<Result<ScheduleChangeOutcome>> ApplyAsync(
        Guid venueId, bool confirm, DateTime fromUtc, DateTime toUtc, Func<PitchScheduleState, PitchScheduleState> propose,
        Action save, Guid? id, CancellationToken cancellationToken)
    {
        var states = await ScheduleStateLoader.LoadAsync(_dbContext, venueId, null, cancellationToken);
        var nowUtc = _clock.UtcNow;
        var rangeStart = fromUtc < nowUtc ? nowUtc : fromUtc;
        var affected = states.Count == 0
            ? []
            : (await _bookings.GetActiveAsync(states.Keys, rangeStart, toUtc, cancellationToken))
                .Where(b => states.TryGetValue(b.PitchId, out var state)
                    && AvailabilityCalculator.Fits(state, b.StartUtc, b.EndUtc, _clock.CairoTimeZone)
                    && !AvailabilityCalculator.Fits(propose(state), b.StartUtc, b.EndUtc, _clock.CairoTimeZone))
                .ToList();

        if (affected.Count > 0 && !confirm)
        {
            return Result.Success(new ScheduleChangeOutcome(false, null, affected));
        }

        save();
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Result.Success(new ScheduleChangeOutcome(true, id, affected));
    }

    private async Task<Error?> AuthorizeAsync(Guid userId, Guid venueId, Guid? pitchId, string permission, CancellationToken cancellationToken)
    {
        if (!(await _access.GetAccessAsync(userId, venueId, cancellationToken)).Can(permission))
        {
            return VenueAccessErrors.VenueNotFound;
        }

        return pitchId is null || await _dbContext.Pitches.AnyAsync(p => p.Id == pitchId && p.VenueId == venueId && !p.IsDeleted, cancellationToken)
            ? null
            : VenueAccessErrors.PitchNotFound;
    }

    /// <summary>From the start of the first business date to the end of the last one's overnight tail.</summary>
    private (DateTime From, DateTime To) DateRangeUtc(DateOnly start, DateOnly end) =>
        (OperatingSchedule.ConvertToUtc(start.ToDateTime(TimeOnly.MinValue), _clock.CairoTimeZone),
         OperatingSchedule.ConvertToUtc(end.AddDays(2).ToDateTime(TimeOnly.MinValue), _clock.CairoTimeZone));
}

public static class ScheduleStateLoader
{
    /// <summary>Schedule state for every live pitch of the venue (or just <paramref name="pitchId"/>), keyed by pitch.</summary>
    public static async Task<Dictionary<Guid, PitchScheduleState>> LoadAsync(
        VenuesDbContext dbContext, Guid venueId, Guid? pitchId, CancellationToken cancellationToken)
    {
        var pitchIds = await dbContext.Pitches.AsNoTracking()
            .Where(p => p.VenueId == venueId && !p.IsDeleted && (pitchId == null || p.Id == pitchId))
            .Select(p => p.Id)
            .ToListAsync(cancellationToken);
        if (pitchIds.Count == 0)
        {
            return [];
        }

        var venueWeek = await dbContext.OperatingSchedules.AsNoTracking().Where(s => s.VenueId == venueId && !s.IsDeleted).ToListAsync(cancellationToken);
        var pitchWeek = await dbContext.PitchWeeklyHours.AsNoTracking().Where(h => h.VenueId == venueId && !h.IsDeleted).ToListAsync(cancellationToken);
        var exceptions = await dbContext.ScheduleOverrides.AsNoTracking().Where(e => e.VenueId == venueId && !e.IsDeleted).ToListAsync(cancellationToken);
        var rules = await dbContext.BookingRules.AsNoTracking().Where(r => r.VenueId == venueId).ToListAsync(cancellationToken);
        var venueRules = rules.Find(r => r.PitchId == null) ?? BookingRules.Defaults(venueId);

        return pitchIds.ToDictionary(id => id, id => new PitchScheduleState(
            id,
            venueWeek,
            pitchWeek.Where(h => h.PitchId == id).ToList(),
            exceptions.Where(e => e.AppliesTo(id)).ToList(),
            rules.Find(r => r.PitchId == id) ?? venueRules));
    }
}
