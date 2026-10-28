using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.PaymentMethods.DeletePaymentMethod;

public sealed class DeletePaymentMethodHandler : ICommandHandler<DeletePaymentMethodCommand, Result>
{
    private readonly IVenueAccessService _accessService;
    private readonly IVenueAuditLogger _auditLogger;
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;

    public DeletePaymentMethodHandler(IVenueAccessService accessService, IVenueAuditLogger auditLogger, VenuesDbContext dbContext, IClock clock)
    {
        _accessService = accessService;
        _auditLogger = auditLogger;
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result> HandleAsync(DeletePaymentMethodCommand command, CancellationToken cancellationToken = default)
    {
        var access = await _accessService.GetAccessAsync(command.UserId, command.VenueId, cancellationToken);
        if (!access.Can(Permissions.PaymentMethodsManage))
        {
            return Result.Failure(VenueAccessErrors.VenueNotFound);
        }

        var method = await _dbContext.PaymentMethods.FirstOrDefaultAsync(
            m => m.Id == command.PaymentMethodId && m.VenueId == command.VenueId && !m.IsDeleted, cancellationToken);
        if (method is null)
        {
            return Result.Failure(PaymentMethodErrors.NotFound);
        }

        // Soft delete: existing bookings may reference the method they were paid through.
        method.MarkDeleted(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "PaymentMethod.Deleted", nameof(VenuePaymentMethod), method.Id, command.UserId, new { method.VenueId }, cancellationToken);

        return Result.Success();
    }
}
