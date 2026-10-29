using System.Text.RegularExpressions;

namespace Malabak.Modules.Venues.Domain;

/// <summary>Manual payment channels a venue can accept deposits through (PRD §3 Payments).</summary>
public static partial class PaymentMethodType
{
    public const string BankTransfer = "BankTransfer";
    public const string MobileWallet = "MobileWallet";
    public const string InstaPay = "InstaPay";

    public static readonly IReadOnlyCollection<string> All = [BankTransfer, MobileWallet, InstaPay];

    public static bool IsValid(string? type) => type is not null && All.Contains(type, StringComparer.Ordinal);

    /// <summary>Format check for the account details of each channel (normalized: no spaces/dashes).</summary>
    public static bool IsValidAccountDetails(string type, string normalizedDetails) => type switch
    {
        BankTransfer => EgyptianIban().IsMatch(normalizedDetails) || BankAccountNumber().IsMatch(normalizedDetails),
        MobileWallet => EgyptianMobile().IsMatch(normalizedDetails),
        InstaPay => InstaPayAddress().IsMatch(normalizedDetails) || EgyptianMobile().IsMatch(normalizedDetails),
        _ => false
    };

    public static string Normalize(string details) =>
        details.Replace(" ", string.Empty, StringComparison.Ordinal).Replace("-", string.Empty, StringComparison.Ordinal).Trim();

    [GeneratedRegex("^EG\\d{27}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex EgyptianIban();

    [GeneratedRegex("^\\d{8,20}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex BankAccountNumber();

    [GeneratedRegex("^(\\+20|0)1[0125]\\d{8}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex EgyptianMobile();

    [GeneratedRegex("^[A-Za-z0-9._]{3,50}@instapay$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase, matchTimeoutMilliseconds: 100)]
    private static partial Regex InstaPayAddress();
}
