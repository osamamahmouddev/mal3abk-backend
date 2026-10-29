using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.PaymentMethods.UpdatePaymentMethod;

public sealed record UpdatePaymentMethodCommand(
    Guid VenueId,
    Guid PaymentMethodId,
    Guid UserId,
    string MethodType,
    string RecipientName,
    string AccountDetails,
    string? Instructions,
    DepositType DepositType,
    decimal DepositValue,
    bool IsActive) : ICommand<Result<PaymentMethodDto>>;
