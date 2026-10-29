namespace Malabak.SharedKernel.Reference;

public sealed record LocalizedItem(string Code, string NameAr, string NameEn);

public sealed record Governorate(string Code, string NameAr, string NameEn, IReadOnlyList<LocalizedItem> Areas);

/// <summary>
/// Versioned reference catalog (changes ship with the code and are reviewed like code). Codes are stable identifiers;
/// names are for display. Areas are curated for the launch cities; other governorates accept any area name.
/// </summary>
public static class EgyptReferenceData
{
    public const int Version = 1;

    public static readonly IReadOnlyList<Governorate> Governorates =
    [
        new("cairo", "القاهرة", "Cairo",
        [
            new("nasr_city", "مدينة نصر", "Nasr City"), new("heliopolis", "مصر الجديدة", "Heliopolis"), new("maadi", "المعادي", "Maadi"),
            new("new_cairo", "القاهرة الجديدة", "New Cairo"), new("zamalek", "الزمالك", "Zamalek"), new("shubra", "شبرا", "Shubra"),
            new("mokattam", "المقطم", "Mokattam"), new("downtown", "وسط البلد", "Downtown"), new("ain_shams", "عين شمس", "Ain Shams"),
            new("el_rehab", "الرحاب", "El Rehab"), new("madinaty", "مدينتي", "Madinaty"), new("shorouk", "الشروق", "El Shorouk")
        ]),
        new("giza", "الجيزة", "Giza",
        [
            new("dokki", "الدقي", "Dokki"), new("mohandessin", "المهندسين", "Mohandessin"), new("haram", "الهرم", "Haram"),
            new("faisal", "فيصل", "Faisal"), new("sheikh_zayed", "الشيخ زايد", "Sheikh Zayed"), new("october", "6 أكتوبر", "6th of October"),
            new("imbaba", "إمبابة", "Imbaba"), new("agouza", "العجوزة", "Agouza")
        ]),
        new("alexandria", "الإسكندرية", "Alexandria",
        [
            new("smouha", "سموحة", "Smouha"), new("sidi_gaber", "سيدي جابر", "Sidi Gaber"), new("miami", "ميامي", "Miami"),
            new("montaza", "المنتزه", "Montaza"), new("agami", "العجمي", "Agami"), new("stanley", "ستانلي", "Stanley")
        ]),
        new("qalyubia", "القليوبية", "Qalyubia", []),
        new("sharqia", "الشرقية", "Sharqia", []),
        new("dakahlia", "الدقهلية", "Dakahlia", []),
        new("gharbia", "الغربية", "Gharbia", []),
        new("monufia", "المنوفية", "Monufia", []),
        new("beheira", "البحيرة", "Beheira", []),
        new("kafr_el_sheikh", "كفر الشيخ", "Kafr El Sheikh", []),
        new("damietta", "دمياط", "Damietta", []),
        new("port_said", "بورسعيد", "Port Said", []),
        new("ismailia", "الإسماعيلية", "Ismailia", []),
        new("suez", "السويس", "Suez", []),
        new("north_sinai", "شمال سيناء", "North Sinai", []),
        new("south_sinai", "جنوب سيناء", "South Sinai", []),
        new("red_sea", "البحر الأحمر", "Red Sea", []),
        new("fayoum", "الفيوم", "Fayoum", []),
        new("beni_suef", "بني سويف", "Beni Suef", []),
        new("minya", "المنيا", "Minya", []),
        new("asyut", "أسيوط", "Asyut", []),
        new("sohag", "سوهاج", "Sohag", []),
        new("qena", "قنا", "Qena", []),
        new("luxor", "الأقصر", "Luxor", []),
        new("aswan", "أسوان", "Aswan", []),
        new("new_valley", "الوادي الجديد", "New Valley", []),
        new("matrouh", "مطروح", "Matrouh", [])
    ];

    public static readonly IReadOnlyList<LocalizedItem> Facilities =
    [
        new("lighting", "إضاءة ليلية", "Floodlights"), new("parking", "موقف سيارات", "Parking"),
        new("changing_rooms", "غرف تغيير ملابس", "Changing rooms"), new("showers", "دُش", "Showers"),
        new("toilets", "دورات مياه", "Toilets"), new("cafeteria", "كافيتريا", "Cafeteria"),
        new("drinking_water", "مياه شرب", "Drinking water"), new("seating", "مقاعد للجمهور", "Spectator seating"),
        new("prayer_room", "مصلى", "Prayer room"), new("first_aid", "إسعافات أولية", "First aid"),
        new("ball_rental", "تأجير كرة", "Ball rental"), new("bibs", "بِبات (صديريات)", "Bibs"), new("wifi", "واي فاي", "Wi-Fi")
    ];

    public static readonly IReadOnlyList<LocalizedItem> PitchTypes =
    [
        new("5v5", "خماسي", "5-a-side"), new("6v6", "سداسي", "6-a-side"), new("7v7", "سباعي", "7-a-side"),
        new("8v8", "ثماني", "8-a-side"), new("9v9", "تساعي", "9-a-side"), new("11v11", "ملعب كامل", "11-a-side (full pitch)")
    ];

    public static readonly IReadOnlyList<LocalizedItem> Surfaces =
    [
        new("ArtificialTurf", "نجيل صناعي", "Artificial turf"), new("NaturalGrass", "نجيل طبيعي", "Natural grass"),
        new("Hybrid", "نجيل هجين", "Hybrid grass"), new("Tartan", "ترتان / مطاطي", "Tartan / rubber"),
        new("HardCourt", "أسمنتي / صلب", "Hard court"), new("Parquet", "باركيه صالات", "Indoor parquet"), new("Sand", "رملي / شاطئي", "Beach sand")
    ];

    /// <summary>Finds a governorate by code or by its Arabic/English name (case-insensitive, trimmed).</summary>
    public static Governorate? FindGovernorate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var key = value.Trim();
        return Governorates.FirstOrDefault(g => Matches(g.Code, g.NameAr, g.NameEn, key));
    }

    /// <summary>
    /// True when the area is acceptable for the governorate: governorates with a curated list require a listed area
    /// (by code or name); the others accept any non-empty area name.
    /// </summary>
    public static bool IsValidArea(Governorate governorate, string? area)
    {
        ArgumentNullException.ThrowIfNull(governorate);
        if (string.IsNullOrWhiteSpace(area))
        {
            return false;
        }

        return governorate.Areas.Count == 0 || governorate.Areas.Any(a => Matches(a.Code, a.NameAr, a.NameEn, area.Trim()));
    }

    public static bool IsKnownFacility(string? code) =>
        !string.IsNullOrWhiteSpace(code) && Facilities.Any(f => Matches(f.Code, f.NameAr, f.NameEn, code.Trim()));

    private static bool Matches(string code, string nameAr, string nameEn, string key) =>
        string.Equals(code, key, StringComparison.OrdinalIgnoreCase)
        || string.Equals(nameEn, key, StringComparison.OrdinalIgnoreCase)
        || string.Equals(nameAr, key, StringComparison.Ordinal);
}
