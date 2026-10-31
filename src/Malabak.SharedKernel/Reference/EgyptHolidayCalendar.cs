using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Globalization;

namespace Malabak.SharedKernel.Reference;

/// <param name="IsEstimated">True for Islamic holidays: the official date follows moon sighting and may shift by a day.</param>
public sealed record Holiday(DateOnly Date, string Code, string NameAr, string NameEn, bool IsEstimated);

/// <summary>
/// Egyptian public holidays for pricing rules (holiday rates). Fixed civil dates and Coptic dates are exact;
/// Islamic dates come from the Umm al-Qura calendar and are flagged as estimates until officially announced.
/// Note: when the government moves a holiday to a Thursday/Sunday, pricing still uses the date listed here.
/// </summary>
public static class EgyptHolidayCalendar
{
    private static readonly UmAlQuraCalendar _hijri = new();
    private static readonly ConcurrentDictionary<int, FrozenSet<DateOnly>> _datesByYear = new();

    public static IReadOnlyList<Holiday> ForYear(int year)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(year, 2000);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(year, 2100);

        var holidays = new List<Holiday>
        {
            Fixed(year, 1, 7, "coptic_christmas", "عيد الميلاد المجيد", "Coptic Christmas"),
            Fixed(year, 1, 25, "revolution_jan25", "عيد ثورة 25 يناير وعيد الشرطة", "January 25 Revolution & Police Day"),
            Fixed(year, 4, 25, "sinai_liberation", "عيد تحرير سيناء", "Sinai Liberation Day"),
            Fixed(year, 5, 1, "labour_day", "عيد العمال", "Labour Day"),
            Fixed(year, 6, 30, "revolution_jun30", "ذكرى ثورة 30 يونيو", "June 30 Revolution"),
            Fixed(year, 7, 23, "revolution_jul23", "عيد ثورة 23 يوليو", "July 23 Revolution"),
            Fixed(year, 10, 6, "armed_forces", "عيد القوات المسلحة", "Armed Forces Day"),
            new(OrthodoxEaster(year).AddDays(1), "sham_el_nessim", "شم النسيم", "Sham El-Nessim", IsEstimated: false)
        };

        holidays.AddRange(Islamic(year, 10, 1, 3, "eid_al_fitr", "عيد الفطر", "Eid al-Fitr"));
        holidays.AddRange(Islamic(year, 12, 9, 1, "arafat", "وقفة عرفات", "Arafat Day"));
        holidays.AddRange(Islamic(year, 12, 10, 4, "eid_al_adha", "عيد الأضحى", "Eid al-Adha"));
        holidays.AddRange(Islamic(year, 1, 1, 1, "islamic_new_year", "رأس السنة الهجرية", "Islamic New Year"));
        holidays.AddRange(Islamic(year, 3, 12, 1, "mawlid", "المولد النبوي الشريف", "Prophet's Birthday"));

        return holidays.OrderBy(h => h.Date).ToList();
    }

    /// <summary>Hot path (every priced 15-minute slice): holiday dates are computed once per year and then looked up.</summary>
    public static bool IsHoliday(DateOnly date) =>
        _datesByYear.GetOrAdd(date.Year, year => ForYear(year).Select(h => h.Date).ToFrozenSet()).Contains(date);

    /// <summary>Orthodox (Julian computus) Easter, converted to the Gregorian date used in Egypt.</summary>
    public static DateOnly OrthodoxEaster(int year)
    {
        var a = year % 4;
        var b = year % 7;
        var c = year % 19;
        var d = ((19 * c) + 15) % 30;
        var e = ((2 * a) + (4 * b) - d + 34) % 7;
        var month = (d + e + 114) / 31;
        var day = ((d + e + 114) % 31) + 1;

        // Julian → Gregorian offset is 13 days for 1900–2099.
        return new DateOnly(year, month, day).AddDays(13);
    }

    private static Holiday Fixed(int year, int month, int day, string code, string ar, string en) =>
        new(new DateOnly(year, month, day), code, ar, en, IsEstimated: false);

    /// <summary>All occurrences in the Gregorian year (a Hijri date can fall twice in one Gregorian year, or span two).</summary>
    private static IEnumerable<Holiday> Islamic(int gregorianYear, int hijriMonth, int hijriDay, int days, string code, string ar, string en)
    {
        var firstHijriYear = _hijri.GetYear(new DateTime(gregorianYear, 1, 1, 0, 0, 0, DateTimeKind.Unspecified));
        for (var hijriYear = firstHijriYear; hijriYear <= firstHijriYear + 1; hijriYear++)
        {
            var start = DateOnly.FromDateTime(_hijri.ToDateTime(hijriYear, hijriMonth, hijriDay, 0, 0, 0, 0));
            for (var i = 0; i < days; i++)
            {
                var date = start.AddDays(i);
                if (date.Year == gregorianYear)
                {
                    var suffix = days > 1 ? $" ({i + 1})" : string.Empty;
                    yield return new Holiday(date, code, ar + suffix, en + suffix, IsEstimated: true);
                }
            }
        }
    }
}
