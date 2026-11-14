namespace Malabak.SharedKernel.Reference;

public sealed record LocalizedItem(string Code, string NameAr, string NameEn, bool IsFocus = false);

public sealed record Governorate(string Code, string NameAr, string NameEn, IReadOnlyList<LocalizedItem> Areas);

/// <summary>
/// Versioned reference catalog (changes ship with the code and are reviewed like code). Codes are stable identifiers;
/// names are for display. Covers all 27 Egyptian governorates with standard administrative centers and districts.
/// </summary>
public static class EgyptReferenceData
{
    public const int Version = 3;

    public static readonly IReadOnlyList<Governorate> Governorates =
    [
        new("cairo", "القاهرة", "Cairo",
        [
            new("nasr_city", "مدينة نصر", "Nasr City"),
            new("heliopolis", "مصر الجديدة", "Heliopolis"),
            new("maadi", "المعادي", "Maadi"),
            new("new_cairo", "القاهرة الجديدة", "New Cairo"),
            new("zamalek", "الزمالك", "Zamalek"),
            new("shubra", "شبرا", "Shubra"),
            new("mokattam", "المقطم", "Mokattam"),
            new("downtown", "وسط البلد", "Downtown"),
            new("ain_shams", "عين شمس", "Ain Shams"),
            new("el_rehab", "الرحاب", "El Rehab"),
            new("madinaty", "مدينتي", "Madinaty"),
            new("shorouk", "الشروق", "El Shorouk"),
            new("helwan", "حلوان", "Helwan"),
            new("el_marg", "المرج", "El Marg"),
            new("hadayek_el_kobba", "حدائق القبة", "Hadayek El Kobba"),
            new("zeitoun", "الزيتون", "El Zeitoun"),
            new("abbassia", "العباسية", "Abbassia"),
            new("matareya", "المطرية", "El Matareya"),
            new("el_nozha", "النزهة", "El Nozha"),
            new("badr_city", "مدينة بدر", "Badr City"),
            new("new_capital", "العاصمة الإدارية", "New Administrative Capital")
        ]),
        new("giza", "الجيزة", "Giza",
        [
            new("dokki", "الدقي", "Dokki"),
            new("mohandessin", "المهندسين", "Mohandessin"),
            new("haram", "الهرم", "Haram"),
            new("faisal", "فيصل", "Faisal"),
            new("sheikh_zayed", "الشيخ زايد", "Sheikh Zayed"),
            new("october", "6 أكتوبر", "6th of October"),
            new("imbaba", "إمبابة", "Imbaba"),
            new("agouza", "العجوزة", "Agouza"),
            new("hadayek_al_ahram", "حدائق الأهرام", "Hadayek Al Ahram"),
            new("boulaq_el_dakrour", "بولاق الدكرور", "Boulaq El Dakrour"),
            new("omraniya", "العمرانية", "El Omraniya"),
            new("warraq", "الوراق", "El Warraq"),
            new("hawamdeya", "الحوامدية", "El Hawamdeya"),
            new("badrashein", "البدرشين", "El Badrashein")
        ]),
        new("alexandria", "الإسكندرية", "Alexandria",
        [
            new("smouha", "سموحة", "Smouha"),
            new("sidi_gaber", "سيدي جابر", "Sidi Gaber"),
            new("miami", "ميامي", "Miami"),
            new("montaza", "المنتزه", "Montaza"),
            new("agami", "العجمي", "Agami"),
            new("stanley", "ستانلي", "Stanley"),
            new("laurent", "لوران", "Laurent"),
            new("glem", "جليم", "Glem"),
            new("roushdy", "رشدي", "Roushdy"),
            new("kafr_abdo", "كفر عبده", "Kafr Abdo"),
            new("seyouf", "السيوف", "El Seyouf"),
            new("mandara", "المندرة", "El Mandara"),
            new("ibrahimiya", "الإبراهيمية", "El Ibrahimiya"),
            new("bahari", "بحري والأنفوشي", "Bahari & Anfoushi"),
            new("moharam_bek", "محرم بك", "Moharam Bek"),
            new("borg_el_arab", "برج العرب", "Borg El Arab")
        ]),
        new("qalyubia", "القليوبية", "Qalyubia",
        [
            new("banha", "بنها", "Banha"),
            new("shubra_el_kheima", "شبرا الخيمة", "Shubra El Kheima"),
            new("obour", "مدينة العبور", "El Obour"),
            new("qalyub", "قليوب", "Qalyub"),
            new("qanater", "القناطر الخيرية", "El Qanater El Khayreya"),
            new("toukh", "طوخ", "Toukh"),
            new("khanka", "الخانكة", "El Khanka"),
            new("shebeen_el_qanater", "شبين القناطر", "Shebeen El Qanater"),
            new("kafr_shukr", "كفر شكر", "Kafr Shukr"),
            new("qaha", "قها", "Qaha")
        ]),
        new("sharqia", "الشرقية", "Sharqia",
        [
            new("zagazig", "الزقازيق", "Zagazig"),
            new("tenth_of_ramadan", "العاشر من رمضان", "10th of Ramadan"),
            new("belbeis", "بلبيس", "Belbeis"),
            new("minya_el_qamh", "منيا القمح", "Minya El Qamh"),
            new("faqous", "فاقوس", "Faqous"),
            new("abu_hammad", "أبو حماد", "Abu Hammad"),
            new("abu_kabir", "أبو كبير", "Abu Kabir"),
            new("hihya", "ههيا", "Hihya"),
            new("deyrb_negm", "ديرب نجم", "Deyarb Negm"),
            new("kafr_saqr", "كفر صقر", "Kafr Saqr"),
            new("new_salheya", "الصالحية الجديدة", "New Salheya")
        ]),
        new("dakahlia", "الدقهلية", "Dakahlia",
        [
            new("mansoura", "المنصورة", "Mansoura"),
            new("talkha", "طلخا", "Talkha"),
            new("mit_ghamr", "ميت غمر", "Mit Ghamr"),
            new("senbellawein", "السنبلاوين", "El Senbellawein"),
            new("dekernes", "دكرنس", "Dekernes"),
            new("belqas", "بلقاس", "Belqas"),
            new("sherbin", "شربين", "Sherbin"),
            new("aga", "أجا", "Aga"),
            new("manzala", "المنزلة", "El Manzala"),
            new("gamasa", "جمصة", "Gamasa"),
            new("nabaroh", "نبروه", "Nabaroh")
        ]),
        new("gharbia", "الغربية", "Gharbia",
        [
            new("tanta", "طنطا", "Tanta"),
            new("mahalla", "المحلة الكبرى", "El Mahalla El Kubra"),
            new("kafr_el_zayat", "كفر الزيات", "Kafr El Zayat"),
            new("zefta", "زفتى", "Zefta"),
            new("santa", "السنطة", "El Santa"),
            new("qotour", "قطور", "Qotour"),
            new("basyoun", "بسيون", "Basyoun"),
            new("samanoud", "سمنود", "Samanoud")
        ]),
        new("monufia", "المنوفية", "Monufia",
        [
            new("shebeen_el_koum", "شبين الكوم", "Shebeen El Koum"),
            new("sadat_city", "مدينة السادات", "Sadat City"),
            new("menouf", "منوف", "Menouf"),
            new("ashmoun", "أشمون", "Ashmoun"),
            new("qwesna", "قويسنا", "Qwesna"),
            new("berket_el_sabaa", "بركة السبع", "Berket El Sabaa"),
            new("tala", "تلا", "Tala"),
            new("bagour", "الباجور", "El Bagour"),
            new("shohada", "الشهداء", "El Shohada")
        ]),
        new("beheira", "البحيرة", "Beheira",
        [
            new("damanhour", "دمنهور", "Damanhour"),
            new("kafr_el_dawwar", "كفر الدوار", "Kafr El Dawwar"),
            new("itay_el_baroud", "إيتاي البارود", "Itay El Baroud"),
            new("kom_hamada", "كوم حمادة", "Kom Hamada"),
            new("abu_hummus", "أبو حمص", "Abu Hummus"),
            new("hosh_eissa", "حوش عيسى", "Hosh Eissa"),
            new("delengat", "الدلنجات", "El Delengat"),
            new("wadi_el_natrun", "وادي النطرون", "Wadi El Natrun"),
            new("rosetta", "رشيد", "Rosetta"),
            new("edku", "إدكو", "Edku")
        ]),
        new("kafr_el_sheikh", "كفر الشيخ", "Kafr El Sheikh",
        [
            new("kafr_el_sheikh_city", "كفر الشيخ", "Kafr El Sheikh"),
            new("desouk", "دسوق", "Desouk"),
            new("sidi_salem", "سيدي سالم", "Sidi Salem"),
            new("fouh", "فوه", "Fouh"),
            new("motobas", "مطوبس", "Motobas"),
            new("baltim", "بلطيم", "Baltim"),
            new("bila", "بيلا", "Bila"),
            new("hamoul", "الحامول", "El Hamoul"),
            new("qallin", "قلين", "Qallin"),
            new("reyad", "الرياض", "El Reyad")
        ]),
        new("damietta", "دمياط", "Damietta",
        [
            new("damietta_city", "دمياط", "Damietta"),
            new("ras_el_bar", "رأس البر", "Ras El Bar"),
            new("new_damietta", "دمياط الجديدة", "New Damietta"),
            new("faraskour", "فارسكور", "Faraskour"),
            new("kafr_saad", "كفر سعد", "Kafr Saad"),
            new("zarqa", "الزرقا", "El Zarqa")
        ]),
        new("port_said", "بورسعيد", "Port Said",
        [
            new("port_said_sharq", "حي الشرق", "El Sharq"),
            new("port_said_arab", "حي العرب", "El Arab"),
            new("port_said_manakh", "حي المناخ", "El Manakh"),
            new("port_said_dawahi", "حي الضواحي", "El Dawahi"),
            new("port_said_zohour", "حي الزهور", "El Zohour"),
            new("port_fouad", "بورفؤاد", "Port Fouad")
        ]),
        new("ismailia", "الإسماعيلية", "Ismailia",
        [
            new("ismailia_city", "مدينة الإسماعيلية", "Ismailia"),
            new("qantara_west", "القنطرة غرب", "Qantara West"),
            new("qantara_east", "القنطرة شرق", "Qantara East"),
            new("tal_el_kebir", "التل الكبير", "El Tal El Kebir"),
            new("fayed", "فايد", "Fayed"),
            new("qassasin", "القصاصين", "El Qassasin"),
            new("abu_sweir", "أبو صوير", "Abu Sweir")
        ]),
        new("suez", "السويس", "Suez",
        [
            new("suez_district", "حي السويس", "Suez District"),
            new("arbaeen", "حي الأربعين", "El Arbaeen"),
            new("faisal_suez", "حي فيصل", "Faisal District"),
            new("attaka", "حي عتاقة", "Attaka"),
            new("ganayen", "حي الجناين", "El Ganayen"),
            new("ain_sokhna", "العين السخنة", "Ain Sokhna")
        ]),
        new("north_sinai", "شمال سيناء", "North Sinai",
        [
            new("arish", "العريش", "Arish"),
            new("sheikh_zuweid", "الشيخ زويد", "Sheikh Zuweid"),
            new("rafah", "رفح", "Rafah"),
            new("bir_el_abd", "بئر العبد", "Bir El Abd"),
            new("hasana", "الحسنة", "Al Hasana"),
            new("nakhl", "نخل", "Nakhl")
        ]),
        new("south_sinai", "جنوب سيناء", "South Sinai",
        [
            new("sharm_el_sheikh", "شرم الشيخ", "Sharm El Sheikh"),
            new("dahab", "دهب", "Dahab"),
            new("nuweiba", "نويبع", "Nuweiba"),
            new("taba", "طابا", "Taba"),
            new("tor_sinai", "طور سيناء", "El Tor"),
            new("ras_sedr", "رأس سدر", "Ras Sedr"),
            new("saint_catherine", "سانت كاترين", "Saint Catherine")
        ]),
        new("red_sea", "البحر الأحمر", "Red Sea",
        [
            new("hurghada", "الغردقة", "Hurghada"),
            new("el_gouna", "الجونة", "El Gouna"),
            new("safaga", "سفاجا", "Safaga"),
            new("qoseir", "القصير", "El Qoseir"),
            new("marsa_alam", "مرسى علم", "Marsa Alam"),
            new("ras_gharib", "رأس غارب", "Ras Gharib")
        ]),
        new("fayoum", "الفيوم", "Fayoum",
        [
            new("fayoum_city", "مدينة الفيوم", "Fayoum City"),
            new("new_fayoum", "الفيوم الجديدة", "New Fayoum"),
            new("sinnuris", "سنورس", "Sinnuris"),
            new("itsa", "إطسا", "Itsa"),
            new("tamiya", "طامية", "Tamiya"),
            new("ibshaway", "إبشواي", "Ibshaway"),
            new("youssef_seddik", "يوسف الصديق", "Youssef El Seddik")
        ]),
        new("beni_suef", "بني سويف", "Beni Suef",
        [
            new("beni_suef_city", "مدينة بني سويف", "Beni Suef City"),
            new("new_beni_suef", "بني سويف الجديدة", "New Beni Suef"),
            new("wasta", "الواسطى", "El Wasta"),
            new("nasser", "ناصر", "Nasser"),
            new("ihnasiya", "إهناسيا", "Ihnasiya"),
            new("biba", "ببا", "Biba"),
            new("sumusta", "سمسطا", "Sumusta"),
            new("fashn", "الفشن", "El Fashn")
        ]),
        new("minya", "المنيا", "Minya",
        [
            new("minya_city", "مدينة المنيا", "Minya City"),
            new("new_minya", "المنيا الجديدة", "New Minya"),
            new("maghagha", "مغاغة", "Maghagha"),
            new("beni_mazar", "بني مزار", "Beni Mazar"),
            new("matai", "مطاي", "Matai"),
            new("samalut", "سمالوط", "Samalut"),
            new("abu_qurqas", "أبو قرقاص", "Abu Qurqas"),
            new("mallawi", "ملوي", "Mallawi"),
            new("deir_mawas", "دير مواس", "Deir Mawas")
        ]),
        new("asyut", "أسيوط", "Asyut",
        [
            new("asyut_city", "مدينة أسيوط", "Asyut City"),
            new("new_asyut", "أسيوط الجديدة", "New Asyut"),
            new("dairut", "ديروط", "Dairut"),
            new("qusiya", "القوصية", "El Qusiya"),
            new("abnoub", "أبنوب", "Abnoub"),
            new("manfalut", "منفلوط", "Manfalut"),
            new("fateh", "الفتح", "El Fateh"),
            new("sahel_selim", "ساحل سليم", "Sahel Selim"),
            new("badari", "البداري", "El Badari"),
            new("sedfa", "صدفا", "Sedfa"),
            new("ghanayem", "الغنايم", "El Ghanayem")
        ]),
        new("sohag", "سوهاج", "Sohag",
        [
            new("sohag_city", "مدينة سوهاج", "Sohag City"),
            new("new_sohag", "سوهاج الجديدة", "New Sohag"),
            new("akhmim", "أخميم", "Akhmim"),
            new("tahta", "طهطا", "Tahta"),
            new("girga", "جرجا", "Girga"),
            new("maragha", "المراغة", "El Maragha"),
            new("tama", "طما", "Tama"),
            new("balyana", "البلينا", "El Balyana"),
            new("monsha", "المنشأة", "El Monsha'a"),
            new("dar_el_salam", "دار السلام", "Dar El Salam"),
            new("saqalta", "ساقلتة", "Saqalta")
        ]),
        new("qena", "قنا", "Qena",
        [
            new("qena_city", "مدينة قنا", "Qena City"),
            new("new_qena", "قنا الجديدة", "New Qena"),
            new("nag_hammadi", "نجع حمادي", "Nag Hammadi"),
            new("dishna", "دشنا", "Dishna"),
            new("qus", "قوص", "Qus"),
            new("qift", "قفط", "Qift"),
            new("farshut", "فرشوط", "Farshut"),
            new("abu_tesht", "أبو تشت", "Abu Tesht"),
            new("naqada", "نقادة", "Naqada"),
            new("waqf", "الوقف", "El Waqf")
        ]),
        new("luxor", "الأقصر", "Luxor",
        [
            new("luxor_city", "مدينة الأقصر", "Luxor City"),
            new("new_tiba", "طيبة الجديدة", "New Tiba"),
            new("qurna", "القرنة", "El Qurna"),
            new("armant", "أرمنت", "Armant"),
            new("esna", "إسنا", "Esna"),
            new("zeiniya", "الزينية", "El Zeiniya"),
            new("bayadiya", "البياضية", "El Bayadiya"),
            new("tod", "الطود", "El Tod")
        ]),
        new("aswan", "أسوان", "Aswan",
        [
            new("aswan_city", "مدينة أسوان", "Aswan City"),
            new("new_aswan", "أسوان الجديدة", "New Aswan"),
            new("kom_ombo", "كوم أمبو", "Kom Ombo"),
            new("edfu", "إدفو", "Edfu"),
            new("nasr_el_nuba", "نصر النوبة", "Nasr El Nuba"),
            new("daraw", "دراو", "Daraw"),
            new("abu_simbel", "أبو سمبل", "Abu Simbel")
        ]),
        new("matrouh", "مطروح", "Matrouh",
        [
            new("marsa_matrouh", "مرسى مطروح", "Marsa Matrouh"),
            new("new_alamein", "العلمين الجديدة", "New Alamein"),
            new("north_coast", "الساحل الشمالي", "North Coast"),
            new("sidi_abdel_rahman", "سيدي عبد الرحمن", "Sidi Abdel Rahman"),
            new("dabaa", "الضبعة", "El Dabaa"),
            new("hamam", "الحمام", "El Hamam"),
            new("siwa", "سيوة", "Siwa")
        ]),
        new("new_valley", "الوادي الجديد", "New Valley",
        [
            new("kharga", "الخارجة", "El Kharga"),
            new("dakhla", "الداخلة", "El Dakhla"),
            new("farafra", "الفرافرة", "Farafra"),
            new("baris", "باريس", "Baris"),
            new("balat", "بلاط", "Balat")
        ])
    ];

    public static readonly IReadOnlyList<LocalizedItem> Facilities =
    [
        new("lighting", "إضاءة ليلية", "Floodlights"), new("parking", "موقف سيارات", "Parking"),
        new("changing_rooms", "غرف تغيير ملابس", "Changing rooms"), new("showers", "دُش", "Showers"),
        new("toilets", "دورات مياه", "Toilets"), new("cafeteria", "كافيتريا", "Cafeteria"),
        new("drinking_water", "مياه شرب", "Drinking water"), new("seating", "مقاعد للجمهور", "Spectator seating"),
        new("prayer_room", "مصلى", "Prayer room"), new("first_aid", "إسعافات أولية", "First aid"),
        new("ball_rental", "تأجير كرة", "Ball rental"), new("bibs", "بِبات (صديريات)", "Bibs"), new("wifi", "واي فاي", "Wi-Fi"),
        new("referee", "حَكَم عند الطلب", "Referee on demand")
    ];

    /// <summary>Names earlier venue forms saved as free text, mapped to their facility code.</summary>
    private static readonly IReadOnlyDictionary<string, string> _facilityAliases = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["floodlights"] = "lighting",
        ["lights"] = "lighting",
        ["locker rooms"] = "changing_rooms",
        ["lockers"] = "changing_rooms",
        ["shower"] = "showers",
        ["bibs / pinnies"] = "bibs",
        ["pinnies"] = "bibs",
        ["first aid station"] = "first_aid",
        ["referee on-demand"] = "referee",
        ["referee"] = "referee",
        ["wifi"] = "wifi",
        ["toilet"] = "toilets",
        ["restrooms"] = "toilets"
    };

    /// <summary>The facility a stored value refers to: by code, by either name, or by an older free-text name.</summary>
    public static LocalizedItem? FindFacility(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var key = value.Trim();
        if (_facilityAliases.TryGetValue(key, out var aliased))
        {
            key = aliased;
        }

        return Facilities.FirstOrDefault(f => Matches(f.Code, f.NameAr, f.NameEn, key));
    }

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
    /// True when the area is acceptable for the governorate: any non-empty name. The curated list (plus areas admins and
    /// venues add) is what clients offer first, but a player whose district is not listed can still type it.
    /// </summary>
    public static bool IsValidArea(Governorate governorate, string? area)
    {
        ArgumentNullException.ThrowIfNull(governorate);
        return !string.IsNullOrWhiteSpace(area);
    }

    public static bool IsKnownFacility(string? code) => FindFacility(code) is not null;

    private static bool Matches(string code, string nameAr, string nameEn, string key) =>
        string.Equals(code, key, StringComparison.OrdinalIgnoreCase)
        || string.Equals(nameEn, key, StringComparison.OrdinalIgnoreCase)
        || string.Equals(nameAr, key, StringComparison.Ordinal);
}
