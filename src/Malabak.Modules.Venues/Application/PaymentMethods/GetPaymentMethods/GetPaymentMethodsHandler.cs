using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.PaymentMethods.GetPaymentMethods;

/// <summary>Masked list — never decrypts.</summary>
public sealed class GetPaymentMethodsHandler : IQueryHandler<GetPaymentMethodsQuery, Result<IReadOnlyList<PaymentMethodDto>>>
{
    private readonly IVenueAccessService _accessService;
    private readonly VenuesDbContext _dbContext;

    public GetPaymentMethodsHandler(IVenueAccessService accessService, VenuesDbContext dbContext)
    {
        _accessService = accessService;
        _dbContext = dbContext;
    }

    public async Task<Result<IReadOnlyList<PaymentMethodDto>>> HandleAsync(GetPaymentMethodsQuery query, CancellationToken cancellationToken = default)
    {
        var access = await _accessService.GetAccessAsync(query.UserId, query.VenueId, cancellationToken);
        if (!access.Can(Permissions.VenueView))
        {
            return Result.Failure<IReadOnlyList<PaymentMethodDto>>(VenueAccessErrors.VenueNotFound);
        }

        var methods = await _dbContext.PaymentMethods
            .AsNoTracking()
            .Where(m => m.VenueId == query.VenueId && !m.IsDeleted)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        IReadOnlyList<PaymentMethodDto> dtos = methods.Select(PaymentMethodMapping.ToDto).ToList();
        return Result.Success(dtos);
    }
}
