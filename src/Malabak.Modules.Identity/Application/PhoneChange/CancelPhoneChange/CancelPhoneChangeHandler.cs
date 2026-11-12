using Malabak.Modules.Identity.Domain;
using Malabak.Modules.Identity.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Identity.Application.PhoneChange.CancelPhoneChange;

public sealed class CancelPhoneChangeHandler : ICommandHandler<CancelPhoneChangeCommand, Result>
{
    private readonly IdentityDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<CancelPhoneChangeHandler> _logger;

    public CancelPhoneChangeHandler(IdentityDbContext dbContext, IClock clock, ILogger<CancelPhoneChangeHandler> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    public async Task<Result> HandleAsync(
        CancelPhoneChangeCommand command,
        CancellationToken cancellationToken = default)
    {
        var request = await _dbContext.PhoneChangeRequests
            .FirstOrDefaultAsync(r => r.Id == command.RequestId && r.UserId == command.UserId, cancellationToken);

        if (request is null)
        {
            return Result.Failure(new Error(
                "Identity.PhoneChangeRequestNotFound",
                "Phone change request not found."));
        }

        if (!request.IsOpen)
        {
            return Result.Failure(new Error(
                "Identity.InvalidPhoneChangeState",
                "Only a phone change request that is still in progress can be cancelled."));
        }

        request.Cancel(_clock.UtcNow);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Phone change request {RequestId} cancelled by user {UserId}.",
            request.Id,
            request.UserId);

        return Result.Success();
    }
}
