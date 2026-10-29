namespace Malabak.Modules.Identity.Domain;

public enum PlayerPosition
{
    Goalkeeper = 0,
    Defender = 1,
    Midfielder = 2,
    Forward = 3
}

public enum SkillLevel
{
    Beginner = 0,
    Intermediate = 1,
    Advanced = 2
}

public static class UserLanguages
{
    public const string Arabic = "ar";
    public const string English = "en";

    public static bool IsSupported(string? language) => language is Arabic or English;
}
