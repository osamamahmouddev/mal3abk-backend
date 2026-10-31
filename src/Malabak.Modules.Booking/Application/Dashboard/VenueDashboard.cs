using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Dashboard;

/// <summary>Booking counts for one business date.</summary>
public sealed record DashboardCounts(int TodayBookings, int Confirmed, int PendingDeposit, int PendingProofReview, int Cancelled, int NoShows);

/// <summary>
/// Money for a period, keeping collected and uncollected money apart:
/// <list type="bullet">
/// <item><c>ConfirmedValue</c>: full price of confirmed and completed bookings.</item>
/// <item><c>DepositsCollected</c>: deposits the venue approved and kept (confirmed, completed and no-show bookings).</item>
/// <item><c>DueAtVenue</c>: what confirmed and completed bookings still owe, paid at the venue (value minus their deposit).</item>
/// <item><c>DepositsPendingReview</c>: deposits uploaded but not yet approved; not collected.</item>
/// <item><c>ExpectedValue</c>: confirmed value plus the full price of bookings still waiting for deposit or review.</item>
/// </list>
/// </summary>
public sealed record DashboardRevenue(
    string Currency, decimal ConfirmedValue, decimal DepositsCollected, decimal DueAtVenue, decimal DepositsPendingReview, decimal ExpectedValue);

/// <summary>Occupied share of open pitch-minutes for one business date (0 when the venue is closed).</summary>
public sealed record OccupancyPoint(DateOnly Date, int OpenMinutes, int BookedMinutes, decimal Percent);

public sealed record RevenuePoint(DateOnly Date, decimal ConfirmedValue, decimal DepositsCollected);

/// <summary>A booking as a dashboard row. <c>DepositState</c>: Approved, UnderReview, AwaitingUpload, PayAtVenue or None.</summary>
public sealed record DashboardBooking(
    Guid BookingId, string Reference, Guid PitchId, DateTime StartUtc, DateTime EndUtc, string CustomerName, bool IsManual,
    BookingStatus Status, decimal TotalPrice, decimal DepositAmount, string Currency, string DepositState, DateTime? DeadlineUtc);

public sealed record DashboardActivity(Guid BookingId, string Reference, BookingStatus FromStatus, BookingStatus ToStatus, DateTime ChangedAtUtc, string? Reason);

public sealed record VenueDashboardDto(
    DateOnly BusinessDate,
    DashboardCounts Today,
    DashboardRevenue? TodayRevenue,
    DashboardRevenue? MonthRevenue,
    OccupancyPoint TodayOccupancy,
    IReadOnlyList<OccupancyPoint> OccupancyThisWeek,
    IReadOnlyList<OccupancyPoint> OccupancyLastWeek,
    IReadOnlyList<RevenuePoint>? RevenueTrend,
    IReadOnlyList<DashboardBooking> Timeline,
    IReadOnlyList<DashboardBooking> Upcoming,
    IReadOnlyList<DashboardBooking> ProofsAwaitingReview,
    IReadOnlyList<DashboardActivity> RecentActivity,
    DateTime GeneratedAtUtc);

/// <param name="IncludeRevenue">False for staff without the reports permission: money figures are left out (null).</param>
public sealed record GetVenueDashboardQuery(Guid VenueId, DateOnly? BusinessDate, bool IncludeRevenue) : IQuery<Result<VenueDashboardDto>>;

public static class VenueDashboardErrors
{
    public static readonly Error DateOutOfRange = new("Dashboard.DateOutOfRange", "Pick a date within a year of today.");
}

/// <summary>
/// Venue dashboard overview (TASK-501). Periods are Cairo business dates: "today" is the requested date, "month" is that
/// calendar month up to its end, the occupancy chart is the 7 days ending on the date and the 7 before, and the revenue trend
/// is the 30 days ending on the date. Bookings belong to the date their start time falls on. Venue isolation comes from the
/// route filter plus the <c>VenueId</c> condition on every query.
/// </summary>
public sealed class GetVenueDashboardHandler : IQueryHandler<GetVenueDashboardQuery, Result<VenueDashboardDto>>
{
    public const int ListSize = 10;

    /// <summary>Statuses that hold the pitch (count toward occupancy and today's bookings).</summary>
    public static readonly BookingStatus[] OccupyingStatuses =
        [BookingStatus.PendingDeposit, BookingStatus.ProofSubmitted, BookingStatus.UnderReview, BookingStatus.Confirmed, BookingStatus.Completed, BookingStatus.NoShow];

    private static readonly BookingStatus[] _confirmed = [BookingStatus.Confirmed, BookingStatus.Completed];
    private static readonly BookingStatus[] _depositKept = [BookingStatus.Confirmed, BookingStatus.Completed, BookingStatus.NoShow];
    private static readonly BookingStatus[] _inReview = [BookingStatus.ProofSubmitted, BookingStatus.UnderReview];

    private readonly IBookingDbContext _dbContext;
    private readonly IVenueCapacityReader _capacity;
    private readonly IUserAccountsContract _users;
    private readonly IClock _clock;

    public GetVenueDashboardHandler(IBookingDbContext dbContext, IVenueCapacityReader capacity, IUserAccountsContract users, IClock clock)
    {
        _dbContext = dbContext;
        _capacity = capacity;
        _users = users;
        _clock = clock;
    }

    public async Task<Result<VenueDashboardDto>> HandleAsync(GetVenueDashboardQuery query, CancellationToken cancellationToken = default)
    {
        var today = _clock.CairoToday;
        var date = query.BusinessDate ?? today;
        if (Math.Abs(date.DayNumber - today.DayNumber) > 366)
        {
            return Result.Failure<VenueDashboardDto>(VenueDashboardErrors.DateOutOfRange);
        }

        var tz = _clock.CairoTimeZone;
        var monthStart = new DateOnly(date.Year, date.Month, 1);
        var windowStart = Min(date.AddDays(-29), date.AddDays(-13), monthStart);
        var windowEnd = Max(date, monthStart.AddMonths(1).AddDays(-1));
        var nowUtc = _clock.UtcNow;

        // One read of the bookings that start inside every period shown (at most ~2 months for one venue).
        var bookings = await _dbContext.Bookings.AsNoTracking()
            .Where(b => b.VenueId == query.VenueId && !b.IsDeleted && b.StartUtc >= ToUtc(windowStart, tz) && b.StartUtc < ToUtc(windowEnd.AddDays(1), tz))
            .Select(b => new Row(b.Id, b.BookingReference, b.PitchId, b.PlayerId, b.StartUtc, b.EndUtc, b.Status, b.TotalPrice.Amount,
                b.DepositAmount.Amount, b.TotalPrice.Currency, b.HoldExpiresAtUtc, b.IsManualBooking, b.CustomerName))
            .ToListAsync(cancellationToken);
        var live = bookings.Where(b => IsLive(b, nowUtc)).ToList();
        var byDate = live.ToLookup(b => LocalDate(b.StartUtc, tz));

        var openMinutes = await _capacity.GetOpenMinutesAsync(query.VenueId, date.AddDays(-13), date, null, cancellationToken);
        var occupancy = Enumerable.Range(0, 14).Select(i => date.AddDays(i - 13)).Select(d => Occupancy(d, openMinutes, byDate[d])).ToList();

        var upcomingRows = await _dbContext.Bookings.AsNoTracking()
            .Where(b => b.VenueId == query.VenueId && !b.IsDeleted && b.StartUtc >= nowUtc && OccupyingStatuses.Contains(b.Status))
            .OrderBy(b => b.StartUtc)
            .Take(ListSize * 2)
            .Select(b => new Row(b.Id, b.BookingReference, b.PitchId, b.PlayerId, b.StartUtc, b.EndUtc, b.Status, b.TotalPrice.Amount,
                b.DepositAmount.Amount, b.TotalPrice.Currency, b.HoldExpiresAtUtc, b.IsManualBooking, b.CustomerName))
            .ToListAsync(cancellationToken);
        var upcoming = upcomingRows.Where(b => IsLive(b, nowUtc)).Take(ListSize).ToList();

        var proofRows = await _dbContext.Bookings.AsNoTracking()
            .Where(b => b.VenueId == query.VenueId && !b.IsDeleted && _inReview.Contains(b.Status))
            .OrderBy(b => b.HoldExpiresAtUtc ?? b.StartUtc)
            .Take(ListSize)
            .Select(b => new Row(b.Id, b.BookingReference, b.PitchId, b.PlayerId, b.StartUtc, b.EndUtc, b.Status, b.TotalPrice.Amount,
                b.DepositAmount.Amount, b.TotalPrice.Currency, b.HoldExpiresAtUtc, b.IsManualBooking, b.CustomerName))
            .ToListAsync(cancellationToken);

        var activity = await _dbContext.StatusHistories.AsNoTracking()
            .Join(_dbContext.Bookings.Where(b => b.VenueId == query.VenueId && !b.IsDeleted), h => h.BookingId, b => b.Id,
                (h, b) => new DashboardActivity(b.Id, b.BookingReference, h.FromStatus, h.ToStatus, h.ChangedAtUtc, h.Reason))
            .OrderByDescending(a => a.ChangedAtUtc)
            .Take(ListSize)
            .ToListAsync(cancellationToken);

        var timelineRows = byDate[date].Where(b => OccupyingStatuses.Contains(b.Status)).OrderBy(b => b.StartUtc).ToList();
        var names = await NamesAsync(timelineRows.Concat(upcoming).Concat(proofRows), cancellationToken);
        var currency = bookings.FirstOrDefault()?.Currency ?? Money.DefaultCurrency;
        var todayRows = bookings.Where(b => LocalDate(b.StartUtc, tz) == date).ToList();
        var monthRows = live.Where(b => LocalDate(b.StartUtc, tz) >= monthStart && LocalDate(b.StartUtc, tz) < monthStart.AddMonths(1)).ToList();

        return Result.Success(new VenueDashboardDto(
            date,
            new DashboardCounts(
                timelineRows.Count,
                todayRows.Count(b => _confirmed.Contains(b.Status)),
                todayRows.Count(b => b.Status == BookingStatus.PendingDeposit && IsLive(b, nowUtc)),
                todayRows.Count(b => _inReview.Contains(b.Status)),
                todayRows.Count(b => b.Status == BookingStatus.Cancelled),
                todayRows.Count(b => b.Status == BookingStatus.NoShow)),
            query.IncludeRevenue ? Revenue(byDate[date], currency) : null,
            query.IncludeRevenue ? Revenue(monthRows, currency) : null,
            occupancy[13],
            occupancy.Skip(7).ToList(),
            occupancy.Take(7).ToList(),
            query.IncludeRevenue
                ? Enumerable.Range(0, 30).Select(i => date.AddDays(i - 29)).Select(d => Trend(d, byDate[d])).ToList()
                : null,
            timelineRows.Select(b => ToDto(b, names)).ToList(),
            upcoming.Select(b => ToDto(b, names)).ToList(),
            proofRows.Select(b => ToDto(b, names)).ToList(),
            activity,
            nowUtc));
    }

    /// <summary>Revenue figures for a set of bookings (see <see cref="DashboardRevenue"/> for the definitions).</summary>
    public static DashboardRevenue Revenue(IEnumerable<Row> rows, string currency)
    {
        var list = rows.ToList();
        var confirmed = list.Where(b => _confirmed.Contains(b.Status)).ToList();
        var confirmedValue = confirmed.Sum(b => b.Total);
        var waiting = list.Where(b => b.Status == BookingStatus.PendingDeposit || _inReview.Contains(b.Status)).Sum(b => b.Total);
        return new DashboardRevenue(
            currency,
            confirmedValue,
            list.Where(b => _depositKept.Contains(b.Status)).Sum(b => b.Deposit),
            confirmedValue - confirmed.Sum(b => b.Deposit),
            list.Where(b => _inReview.Contains(b.Status)).Sum(b => b.Deposit),
            confirmedValue + waiting);
    }

    private static OccupancyPoint Occupancy(DateOnly date, IReadOnlyDictionary<DateOnly, int> open, IEnumerable<Row> rows)
    {
        var openMinutes = open.GetValueOrDefault(date);
        var booked = rows.Where(b => OccupyingStatuses.Contains(b.Status)).Sum(b => (int)(b.EndUtc - b.StartUtc).TotalMinutes);
        var percent = openMinutes == 0 ? 0 : Math.Min(100m, Math.Round(booked * 100m / openMinutes, 1));
        return new OccupancyPoint(date, openMinutes, booked, percent);
    }

    private static RevenuePoint Trend(DateOnly date, IEnumerable<Row> rows)
    {
        var list = rows.ToList();
        return new RevenuePoint(date, list.Where(b => _confirmed.Contains(b.Status)).Sum(b => b.Total), list.Where(b => _depositKept.Contains(b.Status)).Sum(b => b.Deposit));
    }

    private async Task<IReadOnlyDictionary<Guid, string>> NamesAsync(IEnumerable<Row> rows, CancellationToken cancellationToken)
    {
        var players = rows.Where(b => !b.IsManual && string.IsNullOrWhiteSpace(b.CustomerName)).Select(b => b.PlayerId).Distinct().ToList();
        return players.Count == 0 ? new Dictionary<Guid, string>() : await _users.GetFullNamesAsync(players, cancellationToken);
    }

    private static DashboardBooking ToDto(Row b, IReadOnlyDictionary<Guid, string> names) => new(
        b.Id, b.Reference, b.PitchId, b.StartUtc, b.EndUtc,
        !string.IsNullOrWhiteSpace(b.CustomerName) ? b.CustomerName! : names.GetValueOrDefault(b.PlayerId, ""),
        b.IsManual, b.Status, b.Total, b.Deposit, b.Currency, DepositState(b), _inReview.Contains(b.Status) || b.Status == BookingStatus.PendingDeposit ? b.HoldExpiresAtUtc : null);

    private static string DepositState(Row b) => b.Status switch
    {
        _ when b.IsManual => "PayAtVenue",
        BookingStatus.PendingDeposit => "AwaitingUpload",
        BookingStatus.ProofSubmitted or BookingStatus.UnderReview => "UnderReview",
        BookingStatus.Confirmed or BookingStatus.Completed or BookingStatus.NoShow => b.Deposit > 0 ? "Approved" : "PayAtVenue",
        _ => "None"
    };

    /// <summary>Lapsed holds still read "PendingDeposit" until the expiry job runs; they no longer hold the pitch.</summary>
    private static bool IsLive(Row b, DateTime nowUtc) =>
        b.Status != BookingStatus.PendingDeposit || b.HoldExpiresAtUtc is null || b.HoldExpiresAtUtc > nowUtc;

    private static DateOnly LocalDate(DateTime utc, TimeZoneInfo tz) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, tz));

    /// <summary>Start of the business date in UTC. Cairo's spring DST jump skips 00:00, so a missing midnight moves to 01:00.</summary>
    public static DateTime ToUtc(DateOnly date, TimeZoneInfo tz)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return TimeZoneInfo.ConvertTimeToUtc(tz.IsInvalidTime(local) ? local.AddHours(1) : local, tz);
    }

    private static DateOnly Min(params DateOnly[] dates) => dates.Min();

    private static DateOnly Max(params DateOnly[] dates) => dates.Max();

    public sealed record Row(
        Guid Id, string Reference, Guid PitchId, Guid PlayerId, DateTime StartUtc, DateTime EndUtc, BookingStatus Status, decimal Total,
        decimal Deposit, string Currency, DateTime? HoldExpiresAtUtc, bool IsManual, string? CustomerName);
}
