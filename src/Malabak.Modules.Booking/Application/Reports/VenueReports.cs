using System.Globalization;
using System.Text;
using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Application.Dashboard;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Application.Reports;

public enum ReportGrouping
{
    Day = 0,
    Week = 1,
    Month = 2
}

/// <summary>Bookings that start in one period (a day, a week starting Saturday, or a calendar month).</summary>
public sealed record BookingsBucket(DateOnly PeriodStart, int Total, int Confirmed, int Completed, int Pending, int Cancelled, int NoShows, int ExpiredOrRejected);

/// <summary>
/// Revenue split:
/// <list type="bullet">
/// <item><c>Booked</c>: full price of bookings the venue accepted (confirmed, completed, no-show).</item>
/// <item><c>DepositsReceived</c>: deposits of those bookings (approved and kept).</item>
/// <item><c>Outstanding</c>: what confirmed bookings that have not been played yet still owe at the venue.</item>
/// <item><c>Recognized</c>: money earned: full price of completed bookings plus the kept deposit of no-shows.</item>
/// <item><c>Pending</c>: full price of bookings still waiting for a deposit or its review; not counted above.</item>
/// </list>
/// </summary>
public sealed record RevenueSplit(string Currency, decimal Booked, decimal DepositsReceived, decimal Outstanding, decimal Recognized, decimal Pending);

public sealed record RevenueBucket(DateOnly PeriodStart, decimal Booked, decimal Recognized, decimal DepositsReceived);

public sealed record PitchUtilization(Guid PitchId, string Name, int OpenMinutes, int BookedMinutes, decimal Percent);

/// <summary>Booked hours by Cairo weekday (0 = Sunday) and hour of day: how many bookings covered each hour.</summary>
public sealed record PeakHourCell(int DayOfWeek, int Hour, int Bookings);

public sealed record VenueReportDto(
    DateOnly From,
    DateOnly To,
    Guid? PitchId,
    ReportGrouping GroupBy,
    IReadOnlyList<BookingsBucket> Bookings,
    IReadOnlyDictionary<string, int> StatusDistribution,
    RevenueSplit Revenue,
    IReadOnlyList<RevenueBucket> RevenueSeries,
    IReadOnlyList<PitchUtilization> PitchUtilization,
    IReadOnlyList<PeakHourCell> PeakHours);

public static class ReportErrors
{
    public static readonly Error InvalidRange = new("Report.InvalidRange", "Pick a start date on or before the end date, at most 366 days apart.");
}

/// <summary>
/// Venue reports and CSV export (TASK-504). Dates are Cairo business dates (inclusive) and a booking belongs to the date
/// its start time falls on. Lapsed holds count as expired. Every query is scoped to the venue in the route.
/// </summary>
public sealed class VenueReportService
{
    public const int MaxDays = 366;
    public const int MaxExportRows = 20_000;

    private static readonly BookingStatus[] _accepted = [BookingStatus.Confirmed, BookingStatus.Completed, BookingStatus.NoShow];
    private static readonly BookingStatus[] _waiting = [BookingStatus.PendingDeposit, BookingStatus.ProofSubmitted, BookingStatus.UnderReview];

    private readonly IBookingDbContext _dbContext;
    private readonly IVenueCapacityReader _capacity;
    private readonly IUserAccountsContract _users;
    private readonly IClock _clock;

    public VenueReportService(IBookingDbContext dbContext, IVenueCapacityReader capacity, IUserAccountsContract users, IClock clock)
    {
        _dbContext = dbContext;
        _capacity = capacity;
        _users = users;
        _clock = clock;
    }

    public async Task<Result<VenueReportDto>> GetAsync(
        Guid venueId, DateOnly from, DateOnly to, Guid? pitchId, ReportGrouping groupBy, CancellationToken cancellationToken = default)
    {
        if (!ValidRange(from, to) || !Enum.IsDefined(groupBy))
        {
            return Result.Failure<VenueReportDto>(ReportErrors.InvalidRange);
        }

        var tz = _clock.CairoTimeZone;
        var nowUtc = _clock.UtcNow;
        var rows = (await LoadAsync(venueId, from, to, pitchId, cancellationToken)).Select(r => r with { Status = Effective(r, nowUtc) }).ToList();

        var buckets = rows.GroupBy(r => PeriodStart(LocalDate(r.StartUtc, tz), groupBy)).ToDictionary(g => g.Key, g => g.ToList());
        var periods = Periods(from, to, groupBy).ToList();

        var openByPitch = await _capacity.GetOpenMinutesByPitchAsync(venueId, from, to, cancellationToken);
        var utilization = openByPitch
            .Where(p => pitchId is null || p.PitchId == pitchId)
            .Select(p =>
            {
                var booked = rows.Where(r => r.PitchId == p.PitchId && GetVenueDashboardHandler.OccupyingStatuses.Contains(r.Status))
                    .Sum(r => (int)(r.EndUtc - r.StartUtc).TotalMinutes);
                return new PitchUtilization(p.PitchId, p.Name, p.OpenMinutes, booked,
                    p.OpenMinutes == 0 ? 0 : Math.Min(100m, Math.Round(booked * 100m / p.OpenMinutes, 1)));
            })
            .ToList();

        var currency = rows.FirstOrDefault()?.Currency ?? Money.DefaultCurrency;
        return Result.Success(new VenueReportDto(
            from, to, pitchId, groupBy,
            periods.Select(p => Bucket(p, buckets.GetValueOrDefault(p) ?? [])).ToList(),
            rows.GroupBy(r => r.Status.ToString()).OrderBy(g => g.Key, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Count()),
            Split(rows, currency),
            periods.Select(p =>
            {
                var split = Split(buckets.GetValueOrDefault(p) ?? [], currency);
                return new RevenueBucket(p, split.Booked, split.Recognized, split.DepositsReceived);
            }).ToList(),
            utilization,
            PeakHours(rows, tz)));
    }

    /// <summary>
    /// Bookings in the range as CSV (UTF-8 with BOM so Excel shows Arabic). Cells that a spreadsheet would run as a formula are
    /// prefixed with a quote, so a customer name like <c>=HYPERLINK(...)</c> stays plain text.
    /// </summary>
    public async Task<Result<byte[]>> ExportCsvAsync(Guid venueId, DateOnly from, DateOnly to, Guid? pitchId, CancellationToken cancellationToken = default)
    {
        if (!ValidRange(from, to))
        {
            return Result.Failure<byte[]>(ReportErrors.InvalidRange);
        }

        var tz = _clock.CairoTimeZone;
        var nowUtc = _clock.UtcNow;
        var rows = (await LoadAsync(venueId, from, to, pitchId, cancellationToken)).OrderBy(r => r.StartUtc).Take(MaxExportRows).ToList();
        var players = rows.Where(r => !r.IsManual && string.IsNullOrWhiteSpace(r.CustomerName)).Select(r => r.PlayerId).Distinct().ToList();
        var names = players.Count == 0 ? new Dictionary<Guid, string>() : await _users.GetFullNamesAsync(players, cancellationToken);

        var csv = new StringBuilder();
        csv.AppendLine("Reference,Date,Start,End,PitchId,Customer,Source,Status,Total,Deposit,DepositState,Outstanding,Currency");
        foreach (var r in rows)
        {
            var status = Effective(r, nowUtc);
            var start = TimeZoneInfo.ConvertTimeFromUtc(r.StartUtc, tz);
            var end = TimeZoneInfo.ConvertTimeFromUtc(r.EndUtc, tz);
            var outstanding = status == BookingStatus.Confirmed ? r.Total - r.Deposit : 0m;
            string[] cells =
            [
                r.Reference,
                start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                start.ToString("HH:mm", CultureInfo.InvariantCulture),
                end.ToString("HH:mm", CultureInfo.InvariantCulture),
                r.PitchId.ToString("D"),
                !string.IsNullOrWhiteSpace(r.CustomerName) ? r.CustomerName! : names.GetValueOrDefault(r.PlayerId, ""),
                r.IsManual ? "Manual" : "Online",
                status.ToString(),
                r.Total.ToString("0.00", CultureInfo.InvariantCulture),
                r.Deposit.ToString("0.00", CultureInfo.InvariantCulture),
                DepositState(r, status),
                outstanding.ToString("0.00", CultureInfo.InvariantCulture),
                r.Currency
            ];
            csv.AppendLine(string.Join(',', cells.Select(Cell)));
        }

        return Result.Success(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv.ToString())).ToArray());
    }

    /// <summary>Quotes a CSV cell and neutralizes spreadsheet formula injection.</summary>
    public static string Cell(string value)
    {
        var safe = value.Length > 0 && "=+-@\t\r".Contains(value[0], StringComparison.Ordinal) ? "'" + value : value;
        return safe.IndexOfAny([',', '"', '\n', '\r']) >= 0 || safe != value ? $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : safe;
    }

    public static RevenueSplit Split(IReadOnlyCollection<ReportRow> rows, string currency)
    {
        var accepted = rows.Where(r => _accepted.Contains(r.Status)).ToList();
        return new RevenueSplit(
            currency,
            accepted.Sum(r => r.Total),
            accepted.Sum(r => r.Deposit),
            rows.Where(r => r.Status == BookingStatus.Confirmed).Sum(r => r.Total - r.Deposit),
            rows.Where(r => r.Status == BookingStatus.Completed).Sum(r => r.Total) + rows.Where(r => r.Status == BookingStatus.NoShow).Sum(r => r.Deposit),
            rows.Where(r => _waiting.Contains(r.Status)).Sum(r => r.Total));
    }

    public static DateOnly PeriodStart(DateOnly date, ReportGrouping groupBy) => groupBy switch
    {
        // Egyptian weeks start on Saturday.
        ReportGrouping.Week => date.AddDays(-(((int)date.DayOfWeek + 1) % 7)),
        ReportGrouping.Month => new DateOnly(date.Year, date.Month, 1),
        _ => date
    };

    private async Task<List<ReportRow>> LoadAsync(Guid venueId, DateOnly from, DateOnly to, Guid? pitchId, CancellationToken cancellationToken)
    {
        var tz = _clock.CairoTimeZone;
        var startUtc = GetVenueDashboardHandler.ToUtc(from, tz);
        var endUtc = GetVenueDashboardHandler.ToUtc(to.AddDays(1), tz);
        return await _dbContext.Bookings.AsNoTracking()
            .Where(b => b.VenueId == venueId && !b.IsDeleted && b.Status != BookingStatus.Draft && b.StartUtc >= startUtc && b.StartUtc < endUtc
                && (pitchId == null || b.PitchId == pitchId))
            .Select(b => new ReportRow(b.Id, b.BookingReference, b.PitchId, b.PlayerId, b.StartUtc, b.EndUtc, b.Status, b.TotalPrice.Amount,
                b.DepositAmount.Amount, b.TotalPrice.Currency, b.HoldExpiresAtUtc, b.IsManualBooking, b.CustomerName))
            .ToListAsync(cancellationToken);
    }

    private static BookingsBucket Bucket(DateOnly period, List<ReportRow> rows) => new(
        period,
        rows.Count,
        rows.Count(r => r.Status == BookingStatus.Confirmed),
        rows.Count(r => r.Status == BookingStatus.Completed),
        rows.Count(r => _waiting.Contains(r.Status)),
        rows.Count(r => r.Status == BookingStatus.Cancelled),
        rows.Count(r => r.Status == BookingStatus.NoShow),
        rows.Count(r => r.Status is BookingStatus.Expired or BookingStatus.Rejected));

    private static List<PeakHourCell> PeakHours(IEnumerable<ReportRow> rows, TimeZoneInfo tz)
    {
        var counts = new Dictionary<(int Day, int Hour), int>();
        foreach (var r in rows.Where(r => GetVenueDashboardHandler.OccupyingStatuses.Contains(r.Status)))
        {
            // Every hour the booking touches, in Cairo time (a 19:30-21:00 booking counts for 19 and 20).
            for (var hour = TruncateToHour(r.StartUtc); hour < r.EndUtc; hour = hour.AddHours(1))
            {
                var local = TimeZoneInfo.ConvertTimeFromUtc(hour, tz);
                var key = ((int)local.DayOfWeek, local.Hour);
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        return counts.OrderBy(c => c.Key.Day).ThenBy(c => c.Key.Hour).Select(c => new PeakHourCell(c.Key.Day, c.Key.Hour, c.Value)).ToList();
    }

    private static IEnumerable<DateOnly> Periods(DateOnly from, DateOnly to, ReportGrouping groupBy)
    {
        for (var period = PeriodStart(from, groupBy); period <= to; period = groupBy switch
        {
            ReportGrouping.Week => period.AddDays(7),
            ReportGrouping.Month => period.AddMonths(1),
            _ => period.AddDays(1)
        })
        {
            yield return period;
        }
    }

    private static string DepositState(ReportRow r, BookingStatus status) => status switch
    {
        _ when r.IsManual => "PayAtVenue",
        BookingStatus.PendingDeposit => "AwaitingUpload",
        BookingStatus.ProofSubmitted or BookingStatus.UnderReview => "UnderReview",
        BookingStatus.Confirmed or BookingStatus.Completed or BookingStatus.NoShow => r.Deposit > 0 ? "Received" : "PayAtVenue",
        BookingStatus.Cancelled => "Cancelled",
        _ => "None"
    };

    /// <summary>A hold that lapsed but was not expired by the job yet is reported as expired.</summary>
    private static BookingStatus Effective(ReportRow r, DateTime nowUtc) =>
        r.Status == BookingStatus.PendingDeposit && r.HoldExpiresAtUtc is { } expires && expires <= nowUtc ? BookingStatus.Expired : r.Status;

    private static bool ValidRange(DateOnly from, DateOnly to) => to >= from && to.DayNumber - from.DayNumber < MaxDays;

    private static DateOnly LocalDate(DateTime utc, TimeZoneInfo tz) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, tz));

    private static DateTime TruncateToHour(DateTime utc) => new(utc.Year, utc.Month, utc.Day, utc.Hour, 0, 0, DateTimeKind.Utc);

    public sealed record ReportRow(
        Guid Id, string Reference, Guid PitchId, Guid PlayerId, DateTime StartUtc, DateTime EndUtc, BookingStatus Status, decimal Total,
        decimal Deposit, string Currency, DateTime? HoldExpiresAtUtc, bool IsManual, string? CustomerName);
}
