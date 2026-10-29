using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Authorization;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.PaymentMethods.UpdatePaymentMethod;

public sealed class UpdatePaymentMethodHandler : ICommandHandler<UpdatePaymentMethodCommand, Result<PaymentMethodDto>>
{
    private readonly IVenueAccessService _accessService;
    private readonly IFieldCipher _cipher;
    private readonly IVenueAuditLogger _auditLogger;
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;

    public UpdatePaymentMethodHandler(
        IVenueAccessService accessService,
        IFieldCipher cipher,
        IVenueAuditLogger auditLogger,
        VenuesDbContext dbContext,
        IClock clock)
    {
        _accessService = accessService;
        _cipher = cipher;
        _auditLogger = auditLogger;
        _dbContext = dbContext;
        _clock = clock;
    }

    public async Task<Result<PaymentMethodDto>> HandleAsync(UpdatePaymentMethodCommand command, CancellationToken cancellationToken = default)
    {
        var access = await _accessService.GetAccessAsync(command.UserId, command.VenueId, cancellationToken);
        if (!access.Can(Permissions.PaymentMethodsManage))
        {
            return Result.Failure<PaymentMethodDto>(VenueAccessErrors.VenueNotFound);
        }

        var method = await _dbContext.PaymentMethods.FirstOrDefaultAsync(
            m => m.Id == command.PaymentMethodId && m.VenueId == command.VenueId && !m.IsDeleted, cancellationToken);
        if (method is null)
        {
            return Result.Failure<PaymentMethodDto>(PaymentMethodErrors.NotFound);
        }

        var input = PaymentMethodInput.Validate(
            command.MethodType, command.RecipientName, command.AccountDetails, command.Instructions, command.DepositType, command.DepositValue);
        if (input.IsFailure)
        {
            return Result.Failure<PaymentMethodDto>(input.Error);
        }

        var nowUtc = _clock.UtcNow;
        method.Update(command.MethodType, command.RecipientName, command.Instructions, input.Value.Deposit, command.IsActive, nowUtc);
        var ciphertext = await _cipher.EncryptAsync(input.Value.NormalizedDetails, PaymentMethodMapping.EncryptionContext(method.Id), cancellationToken);
        method.SetAccountDetails(ciphertext, input.Value.NormalizedDetails, nowUtc);

        await _dbContext.SaveChangesAsync(cancellationToken);

        await _auditLogger.LogAsync(
            "PaymentMethod.Updated", nameof(VenuePaymentMethod), method.Id, command.UserId,
            new { method.VenueId, method.MethodType, method.IsActive }, cancellationToken);

        return Result.Success(PaymentMethodMapping.ToDto(method));
    }
}
