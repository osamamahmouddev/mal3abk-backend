using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.PaymentMethods.GetPaymentMethods;

public sealed record GetPaymentMethodsQuery(Guid VenueId, Guid UserId) : IQuery<Result<IReadOnlyList<PaymentMethodDto>>>;
