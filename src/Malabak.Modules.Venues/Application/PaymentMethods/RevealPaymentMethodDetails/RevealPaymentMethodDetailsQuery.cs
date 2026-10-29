using Malabak.SharedKernel;

namespace Malabak.Modules.Venues.Application.PaymentMethods.RevealPaymentMethodDetails;

public sealed record RevealPaymentMethodDetailsQuery(Guid VenueId, Guid PaymentMethodId, Guid UserId) : IQuery<Result<PaymentMethodDetailsDto>>;
