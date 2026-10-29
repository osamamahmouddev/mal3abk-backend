using Malabak.Modules.Venues.Domain;
using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.PaymentMethods.CreatePaymentMethod;

public sealed record CreatePaymentMethodCommand(
    Guid VenueId,
    Guid UserId,
    string MethodType,
    string RecipientName,
    string AccountDetails,
    string? Instructions,
    DepositType DepositType,
    decimal DepositValue) : ICommand<Result<PaymentMethodDto>>;
