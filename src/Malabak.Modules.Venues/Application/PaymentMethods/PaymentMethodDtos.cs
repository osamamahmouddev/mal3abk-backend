using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;

namespace Malabak.Modules.Venues.Application.PaymentMethods;

/// <summary>List/summary view: account details only as a mask (e.g. "•••• 4567").</summary>
public sealed record PaymentMethodDto(
    Guid Id,
    Guid VenueId,
    string MethodType,
    string RecipientName,
    string AccountDetailsMasked,
    string? Instructions,
    string DepositType,
    decimal DepositValue,
    bool IsActive);

/// <summary>Revealed view with the decrypted account details; returned only after an access check and audited.</summary>
public sealed record PaymentMethodDetailsDto(
    Guid Id,
    string MethodType,
    string RecipientName,
    string AccountDetails,
    string? Instructions);

public static class PaymentMethodMapping
{
    public static PaymentMethodDto ToDto(VenuePaymentMethod method) => new(
        method.Id,
        method.VenueId,
        method.MethodType,
        method.RecipientName,
        method.AccountDetailsMask,
        method.Instructions,
        method.DepositType.ToString(),
        method.DepositValue,
        method.IsActive);

    public static FieldEncryptionContext EncryptionContext(Guid paymentMethodId) =>
        new(VenuePaymentMethod.TableName, VenuePaymentMethod.AccountDetailsColumn, paymentMethodId);
}

public static class PaymentMethodErrors
{
    public static readonly Error NotFound = new("PaymentMethod.NotFound", "Payment method not found.");
    public static readonly Error InvalidType = new("PaymentMethod.InvalidType", "Payment method type must be BankTransfer, MobileWallet or InstaPay.");
    public static readonly Error InvalidAccountDetails = new("PaymentMethod.InvalidAccountDetails", "Account details are not valid for this payment method type.");
    public static readonly Error InvalidDeposit = new("PaymentMethod.InvalidDeposit", "Deposit rule is not valid.");
    public static readonly Error InvalidRecipient = new("PaymentMethod.InvalidRecipient", "Recipient name is required and must be at most 200 characters.");
    public static readonly Error InvalidInstructions = new("PaymentMethod.InvalidInstructions", "Instructions must be at most 1000 characters.");
}
