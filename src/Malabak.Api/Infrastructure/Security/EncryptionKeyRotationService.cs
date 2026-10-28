using Malabak.SharedKernel.Security;

namespace Malabak.Api.Infrastructure.Security;

/// <summary>
/// Rotation entry points, written to be idempotent and safe to retry so they can run as background jobs.
/// Pending TASK-105: register <see cref="RotateDataKeyAsync"/> and <see cref="ReEncryptAllAsync"/> as Hangfire
/// recurring jobs once the Hangfire infrastructure lands.
/// </summary>
public sealed class EncryptionKeyRotationService
{
    public const int DefaultBatchSize = 200;

    private readonly IDataKeyRing _keyRing;
    private readonly IFieldCipher _cipher;
    private readonly IEnumerable<IReEncryptionTarget> _targets;
    private readonly ILogger<EncryptionKeyRotationService> _logger;

    public EncryptionKeyRotationService(
        IDataKeyRing keyRing,
        IFieldCipher cipher,
        IEnumerable<IReEncryptionTarget> targets,
        ILogger<EncryptionKeyRotationService> logger)
    {
        _keyRing = keyRing;
        _cipher = cipher;
        _targets = targets;
        _logger = logger;
    }

    /// <summary>Activates a new data key, re-wraps keys left on an old KEK, then re-encrypts existing rows.</summary>
    public async Task RotateDataKeyAsync(CancellationToken cancellationToken = default)
    {
        var newKeyId = await _keyRing.RotateAsync(cancellationToken);
        _logger.LogInformation("Data encryption key rotated; new active key {DataKeyId}", newKeyId);

        await RewrapDataKeysAsync(cancellationToken);
        await ReEncryptAllAsync(DefaultBatchSize, cancellationToken);
    }

    /// <summary>Run after changing the current KEK so no data key depends on the retired one any more.</summary>
    public async Task<int> RewrapDataKeysAsync(CancellationToken cancellationToken = default)
    {
        var rewrapped = await _keyRing.RewrapWithCurrentKeyEncryptionKeyAsync(cancellationToken);
        if (rewrapped > 0)
        {
            _logger.LogInformation("Re-wrapped {Count} data encryption keys with the current key-encryption key", rewrapped);
        }

        return rewrapped;
    }

    public async Task<int> ReEncryptAllAsync(int batchSize = DefaultBatchSize, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize);

        var total = 0;
        foreach (var target in _targets)
        {
            int processed;
            do
            {
                processed = await target.ReEncryptBatchAsync(_cipher, batchSize, cancellationToken);
                total += processed;
            }
            while (processed > 0);

            _logger.LogInformation("Re-encryption finished for {Target}", target.Name);
        }

        return total;
    }
}
