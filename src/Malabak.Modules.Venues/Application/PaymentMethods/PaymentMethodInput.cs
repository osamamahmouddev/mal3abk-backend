using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.PaymentMethods;

/// <summary>Shared validation for create/update so both enforce exactly the same rules.</summary>
public static class PaymentMethodInput
{
    public static Result<(string NormalizedDetails, DepositRule Deposit)> Validate(
        string methodType,
        string recipientName,
        string accountDetails,
        string? instructions,
        DepositType depositType,
        decimal depositValue)
    {
        if (!PaymentMethodType.IsValid(methodType))
        {
            return Result.Failure<(string, DepositRule)>(PaymentMethodErrors.InvalidType);
        }

        if (string.IsNullOrWhiteSpace(recipientName) || recipientName.Trim().Length > VenuePaymentMethod.MaxRecipientNameLength)
        {
            return Result.Failure<(string, DepositRule)>(PaymentMethodErrors.InvalidRecipient);
        }

        if (instructions?.Trim().Length > VenuePaymentMethod.MaxInstructionsLength)
        {
            return Result.Failure<(string, DepositRule)>(PaymentMethodErrors.InvalidInstructions);
        }

        var normalized = PaymentMethodType.Normalize(accountDetails ?? string.Empty);
        if (!PaymentMethodType.IsValidAccountDetails(methodType, normalized))
        {
            return Result.Failure<(string, DepositRule)>(PaymentMethodErrors.InvalidAccountDetails);
        }

        if (!Enum.IsDefined(depositType))
        {
            return Result.Failure<(string, DepositRule)>(PaymentMethodErrors.InvalidDeposit);
        }

        try
        {
            return Result.Success((normalized, DepositRule.Create(depositType, depositValue)));
        }
        catch (ArgumentOutOfRangeException)
        {
            return Result.Failure<(string, DepositRule)>(PaymentMethodErrors.InvalidDeposit);
        }
    }
}
