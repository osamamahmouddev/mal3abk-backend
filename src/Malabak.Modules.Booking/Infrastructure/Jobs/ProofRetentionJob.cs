using Hangfire;
using Malabak.Modules.Booking.Infrastructure;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Infrastructure.Jobs;

public sealed class ProofRetentionJob
{
    public const string RecurringJobId = "booking-proof-retention-job";
    public const string CronSchedule = "0 3 * * *"; // Daily at 03:00 UTC

    private readonly BookingDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ILogger<ProofRetentionJob> _logger;

    public ProofRetentionJob(
        BookingDbContext dbContext,
        IClock clock,
        ILogger<ProofRetentionJob> logger)
    {
        _dbContext = dbContext;
        _clock = clock;
        _logger = logger;
    }

    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteRecurringCleanupAsync(CancellationToken cancellationToken = default)
    {
        var retentionCutoff = _clock.UtcNow.AddMonths(-24); // 24-month retention policy

        var oldProofs = await _dbContext.PaymentProofs
            .Where(p => p.CreatedAtUtc < retentionCutoff && !p.IsArchived)
            .Take(100)
            .ToListAsync(cancellationToken);

        if (oldProofs.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Archiving {Count} payment proofs past retention cutoff ({Cutoff})",
            oldProofs.Count, retentionCutoff);

        foreach (var proof in oldProofs)
        {
            proof.Archive(_clock.UtcNow);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}
