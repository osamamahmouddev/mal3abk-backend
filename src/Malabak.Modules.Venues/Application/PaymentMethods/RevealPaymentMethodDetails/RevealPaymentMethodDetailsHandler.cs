using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.PaymentMethods.RevealPaymentMethodDetails;

/// <summary>
/// Decrypts account details for users allowed to manage the venue's payment methods, and audits every reveal.
/// Players will see them only inside the booking flow (Phase 3), through the same cipher and AAD.
/// </summary>
public sealed class RevealPaymentMethodDetailsHandler : IQueryHandler<RevealPaymentMethodDetailsQuery, Result<PaymentMethodDetailsDto>>
{
    private readonly IVenueAccessService _accessService;
    private readonly IFieldCipher _cipher;
    private readonly IVenueAuditLogger _auditLogger;
    private readonly VenuesDbContext _dbContext;

    public RevealPaymentMethodDetailsHandler(
        IVenueAccessService accessService,
        IFieldCipher cipher,
        IVenueAuditLogger auditLogger,
        VenuesDbContext dbContext)
    {
        _accessService = accessService;
        _cipher = cipher;
        _auditLogger = auditLogger;
        _dbContext = dbContext;
    }

    public async Task<Result<PaymentMethodDetailsDto>> HandleAsync(RevealPaymentMethodDetailsQuery query, CancellationToken cancellationToken = default)
    {
        var access = await _accessService.GetAccessAsync(query.UserId, query.VenueId, cancellationToken);
        if (!access.Can(Permissions.PaymentMethodsManage))
        {
            return Result.Failure<PaymentMethodDetailsDto>(VenueAccessErrors.VenueNotFound);
        }

        var method = await _dbContext.PaymentMethods
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == query.PaymentMethodId && m.VenueId == query.VenueId && !m.IsDeleted, cancellationToken);
        if (method is null)
        {
            return Result.Failure<PaymentMethodDetailsDto>(PaymentMethodErrors.NotFound);
        }

        var details = await _cipher.DecryptAsync(method.AccountDetailsCiphertext, PaymentMethodMapping.EncryptionContext(method.Id), cancellationToken);

        await _auditLogger.LogAsync(
            "PaymentMethod.DetailsRevealed", nameof(VenuePaymentMethod), method.Id, query.UserId, new { method.VenueId }, cancellationToken);

        return Result.Success(new PaymentMethodDetailsDto(method.Id, method.MethodType, method.RecipientName, details, method.Instructions));
    }
}
