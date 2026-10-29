using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Domain;

/// <summary>
/// A manual payment channel of a venue. Account details (IBAN, wallet number, InstaPay address) are only
/// ever held encrypted; the plaintext is encrypted/decrypted in the application layer with an AAD bound to
/// this row, and only a short mask is stored for display in lists.
/// </summary>
public sealed class VenuePaymentMethod : Entity<Guid>
{
    public const string TableName = "payment_methods";
    public const string AccountDetailsColumn = "account_details";
    public const int MaxRecipientNameLength = 200;
    public const int MaxInstructionsLength = 1000;

    private VenuePaymentMethod()
    {
    }

    public Guid VenueId { get; private set; }
    public string MethodType { get; private set; } = string.Empty;
    public string RecipientName { get; private set; } = string.Empty;
    public byte[] AccountDetailsCiphertext { get; private set; } = [];
    public string AccountDetailsMask { get; private set; } = string.Empty;
    public string? Instructions { get; private set; }
    public DepositType DepositType { get; private set; }
    public decimal DepositValue { get; private set; }
    public bool IsActive { get; private set; }

    public DepositRule DepositRule => DepositRule.Create(DepositType, DepositValue);

    public static VenuePaymentMethod Create(
        Guid id,
        Guid venueId,
        string methodType,
        string recipientName,
        string? instructions,
        DepositRule depositRule,
        DateTime createdAtUtc)
    {
        var method = new VenuePaymentMethod
        {
            Id = id,
            VenueId = venueId,
            IsActive = true,
            CreatedAtUtc = createdAtUtc
        };

        method.Apply(methodType, recipientName, instructions, depositRule);
        return method;
    }

    public void Update(string methodType, string recipientName, string? instructions, DepositRule depositRule, bool isActive, DateTime nowUtc)
    {
        Apply(methodType, recipientName, instructions, depositRule);
        IsActive = isActive;
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Stores already-encrypted account details plus a display mask derived from the plaintext.</summary>
    public void SetAccountDetails(byte[] ciphertext, string normalizedPlaintext, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        ArgumentException.ThrowIfNullOrWhiteSpace(normalizedPlaintext);

        if (ciphertext.Length == 0)
        {
            throw new ArgumentException("Ciphertext is required.", nameof(ciphertext));
        }

        AccountDetailsCiphertext = ciphertext;
        AccountDetailsMask = Mask(normalizedPlaintext);
        UpdatedAtUtc = nowUtc;
    }

    /// <summary>Used by key rotation: same plaintext, new ciphertext under the current data key.</summary>
    public void ReplaceCiphertext(byte[] ciphertext, DateTime nowUtc)
    {
        ArgumentNullException.ThrowIfNull(ciphertext);
        AccountDetailsCiphertext = ciphertext;
        UpdatedAtUtc = nowUtc;
    }

    public void MarkDeleted(DateTime nowUtc)
    {
        IsDeleted = true;
        IsActive = false;
        UpdatedAtUtc = nowUtc;
    }

    public static string Mask(string normalizedPlaintext)
    {
        var visible = normalizedPlaintext.Length <= 4 ? string.Empty : normalizedPlaintext[^4..];
        return $"•••• {visible}".TrimEnd();
    }

    private void Apply(string methodType, string recipientName, string? instructions, DepositRule depositRule)
    {
        ArgumentNullException.ThrowIfNull(depositRule);
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientName);

        if (!PaymentMethodType.IsValid(methodType))
        {
            throw new ArgumentException("Unknown payment method type.", nameof(methodType));
        }

        if (recipientName.Trim().Length > MaxRecipientNameLength)
        {
            throw new ArgumentException("Recipient name is too long.", nameof(recipientName));
        }

        if (instructions?.Trim().Length > MaxInstructionsLength)
        {
            throw new ArgumentException("Instructions are too long.", nameof(instructions));
        }

        MethodType = methodType;
        RecipientName = recipientName.Trim();
        Instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim();
        DepositType = depositRule.Type;
        DepositValue = depositRule.Value;
    }
}
