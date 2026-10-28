using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

public sealed record SurfaceType
{
    public const string ArtificialTurfValue = "ArtificialTurf";
    public const string NaturalGrassValue = "NaturalGrass";
    public const string HybridGrassValue = "Hybrid";
    public const string TartanValue = "Tartan";
    public const string HardCourtValue = "HardCourt";
    public const string ParquetValue = "Parquet";
    public const string SandValue = "Sand";

    private static readonly Dictionary<string, (string DisplayName, string ArabicDisplayName)> _allowedTypes =
        new(StringComparer.OrdinalIgnoreCase)
        {
            [ArtificialTurfValue] = ("Artificial Turf", "نجيل صناعي"),
            [NaturalGrassValue] = ("Natural Grass", "نجيل طبيعي"),
            [HybridGrassValue] = ("Hybrid Grass", "نجيل هجين"),
            [TartanValue] = ("Tartan / Rubber", "ترتان / مطاطي"),
            [HardCourtValue] = ("Hard Court / Concrete", "أسمنتي / صلب"),
            [ParquetValue] = ("Indoor Parquet", "باركيه صالات"),
            [SandValue] = ("Beach Sand", "رملي / شاطئي")
        };

    public string Value { get; init; }
    public string DisplayName { get; init; }
    public string ArabicDisplayName { get; init; }

    private SurfaceType(string value, string displayName, string arabicDisplayName)
    {
        Value = value;
        DisplayName = displayName;
        ArabicDisplayName = arabicDisplayName;
    }

    public static Result<SurfaceType> Create(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue))
        {
            return Result.Failure<SurfaceType>(new Error(
                "Pitch.EmptySurfaceType",
                "Pitch surface type cannot be empty."));
        }

        var normalized = rawValue.Trim();
        foreach (var (canonicalKey, names) in _allowedTypes)
        {
            if (string.Equals(canonicalKey, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(names.DisplayName, normalized, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(names.ArabicDisplayName, normalized, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Success(new SurfaceType(canonicalKey, names.DisplayName, names.ArabicDisplayName));
            }
        }

        var validOptions = string.Join(", ", _allowedTypes.Keys);
        return Result.Failure<SurfaceType>(new Error(
            "Pitch.InvalidSurfaceType",
            $"Invalid surface type '{rawValue}'. Allowed types: {validOptions}."));
    }

    public static SurfaceType ArtificialTurf => new(ArtificialTurfValue, "Artificial Turf", "نجيل صناعي");
    public static SurfaceType NaturalGrass => new(NaturalGrassValue, "Natural Grass", "نجيل طبيعي");
    public static SurfaceType HybridGrass => new(HybridGrassValue, "Hybrid Grass", "نجيل هجين");
    public static SurfaceType Tartan => new(TartanValue, "Tartan / Rubber", "ترتان / مطاطي");
    public static SurfaceType HardCourt => new(HardCourtValue, "Hard Court / Concrete", "أسمنتي / صلب");
    public static SurfaceType Parquet => new(ParquetValue, "Indoor Parquet", "باركيه صالات");
    public static SurfaceType Sand => new(SandValue, "Beach Sand", "رملي / شاطئي");

    public override string ToString() => Value;
}
