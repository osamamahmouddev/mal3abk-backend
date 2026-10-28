using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.PaymentMethods.DeletePaymentMethod;

public sealed record DeletePaymentMethodCommand(Guid VenueId, Guid PaymentMethodId, Guid UserId) : ICommand<Result>;
