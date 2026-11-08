namespace Malabak.SharedKernel.Security;

/// <summary>
/// Domain-separation labels mixed into the blind-index HMAC so the same input yields unrelated
/// indexes in different columns (a phone index can't be joined against an email index).
/// </summary>
public static class BlindIndexPurpose
{
    public const string Phone = "phone";
    public const string Email = "email";

    /// <summary>Walk-in customer keys in a venue's customer directory (TASK-503).</summary>
    public const string VenueCustomerPhone = "venue-customer-phone";

    /// <summary>Landing page waitlist phone blind index (TASK-814).</summary>
    public const string WaitlistPhone = "waitlist-phone";
}
