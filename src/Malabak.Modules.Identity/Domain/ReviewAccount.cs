namespace Malabak.Modules.Identity.Domain;

/// <summary>
/// The demo account App Store / Google Play reviewers sign in with (fixed code, no SMS). It must always work: it is never
/// rate-limited, it is restored if a reviewer deletes it while testing, and the platform team cannot suspend or delete it.
/// </summary>
public static class ReviewAccount
{
    public const string PhoneNumber = "+201000000999";
    public const string Code = "123456";

    public static bool Is(string? normalizedPhone) => string.Equals(normalizedPhone, PhoneNumber, StringComparison.Ordinal);
}
