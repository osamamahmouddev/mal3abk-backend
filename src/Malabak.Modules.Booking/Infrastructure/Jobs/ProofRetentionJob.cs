using Hangfire;
using Malabak.Modules.Booking.Application.Abstractions;
using Malabak.Modules.Booking.Domain;
using Malabak.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Malabak.Modules.Booking.Infrastructure.Jobs;

/// <summary>
/// Deletes payment-proof images once the retention period of ADR-0007 is over, because a third party's financial screenshot
/// is kept no longer than it is needed: 90 days for proofs that did not lead to a confirmed booking (rejected, or on a
/// cancelled, rejected or expired booking) and 2 years for approved ones. The proof row stays, without its image, as the
/// audit record and for duplicate-receipt detection. Each proof is handled on its own and retried the next day if its
/// image could not be deleted.
/// </summary>
public sealed class ProofRetentionJob
{
    public const string RecurringJobId = "booking-proof-retention-job";
    public const string CronSchedule = "0 3 * * *"; // Daily at 03:00 UTC
    public const int RejectedRetentionDays = 90;
    public const int ApprovedRetentionDays = 730;
    public const int BatchSize = 100;

    private readonly BookingDbContext _dbContext;
    private readonly IProofFileStore _files;
    private readonly IClock _clock;
    private readonly ILogger<ProofRetentionJob> _logger;

    public ProofRetentionJob(BookingDbContext dbContext, IProofFileStore files, IClock clock, ILogger<ProofRetentionJob> logger)
    {
        _dbContext = dbContext;
        _files = files;
        _clock = clock;
        _logger = logger;
    }

    [Queue("low")]
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task ExecuteRecurringCleanupAsync(CancellationToken cancellationToken = default)
    {
        var nowUtc = _clock.UtcNow;
        var rejectedCutoff = nowUtc.AddDays(-RejectedRetentionDays);
        var approvedCutoff = nowUtc.AddDays(-ApprovedRetentionDays);
        var notConfirmed = new[] { BookingStatus.Cancelled, BookingStatus.Rejected, BookingStatus.Expired };

        var dueIds = await (
            from booking in _dbContext.Bookings.AsNoTracking()
            from proof in booking.Proofs
            where !proof.IsArchived
                && proof.MediaId != null
                && ((proof.IsApproved == true && proof.UploadedAtUtc < approvedCutoff)
                    || (proof.IsApproved != true && proof.UploadedAtUtc < rejectedCutoff
                        && (proof.IsApproved == false || notConfirmed.Contains(booking.Status))))
            orderby proof.UploadedAtUtc
            select proof.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        if (dueIds.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Deleting {Count} payment proof images past their retention period", dueIds.Count);

        foreach (var id in dueIds)
        {
            await PurgeAsync(id, nowUtc, cancellationToken);
        }
    }

    private async Task PurgeAsync(Guid proofId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        _dbContext.ChangeTracker.Clear();
        var proof = await _dbContext.PaymentProofs.FirstOrDefaultAsync(p => p.Id == proofId, cancellationToken);
        if (proof is null || proof.IsArchived || proof.MediaId is not { } mediaId)
        {
            return;
        }

        try
        {
            // The image first: if this fails the proof stays due and is tried again tomorrow.
            await _files.DeleteAsync(mediaId, cancellationToken);
            proof.Archive(nowUtc);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Payment proof {ProofId} could not be purged; it will be retried", proofId);
        }
    }
}
