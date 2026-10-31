using Malabak.SharedKernel;

namespace Malabak.Modules.Booking.Domain;

/// <summary>
/// A staff-only note about one customer of one venue. The text may hold personal details, so it is stored encrypted
/// (AES-256-GCM bound to this row); <see cref="CustomerKey"/> never contains a raw phone number.
/// </summary>
public sealed class VenueCustomerNote
{
    public const int MaxLength = 1000;

    public Guid Id { get; private set; }
    public Guid VenueId { get; private set; }
    public string CustomerKey { get; private set; } = string.Empty;
    public byte[] TextCiphertext { get; private set; } = [];
    public Guid AuthorUserId { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTime? DeletedAtUtc { get; private set; }

    private VenueCustomerNote()
    {
    }

    public static VenueCustomerNote Create(Guid id, Guid venueId, string customerKey, Guid authorUserId, DateTime nowUtc) => new()
    {
        Id = id,
        VenueId = venueId,
        CustomerKey = customerKey,
        AuthorUserId = authorUserId,
        CreatedAtUtc = nowUtc
    };

    public void SetCiphertext(byte[] ciphertext) => TextCiphertext = ciphertext;

    public void MarkDeleted(DateTime nowUtc)
    {
        IsDeleted = true;
        DeletedAtUtc = nowUtc;
    }

    public static Guid NewId() => IdGenerator.NewGuid();
}
