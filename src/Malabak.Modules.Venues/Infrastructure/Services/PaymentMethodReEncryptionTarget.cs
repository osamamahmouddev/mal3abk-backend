using Malabak.Modules.Venues.Application.PaymentMethods;
using Malabak.SharedKernel;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Infrastructure.Services;

/// <summary>Re-encrypts payment method account details still under a retired data key.</summary>
public sealed class PaymentMethodReEncryptionTarget : IReEncryptionTarget
{
    private readonly VenuesDbContext _dbContext;
    private readonly IClock _clock;

    public PaymentMethodReEncryptionTarget(VenuesDbContext dbContext, IClock clock)
    {
        _dbContext = dbContext;
        _clock = clock;
    }

    public string Name => "venues.payment_methods";

    public async Task<int> ReEncryptBatchAsync(IFieldCipher cipher, int batchSize, CancellationToken cancellationToken = default)
    {
        // The key id lives inside the ciphertext, so candidates are checked in memory; the table is small per venue.
        var methods = await _dbContext.PaymentMethods.IgnoreQueryFilters().ToListAsync(cancellationToken);

        var processed = 0;
        foreach (var method in methods)
        {
            if (processed >= batchSize)
            {
                break;
            }

            if (!await cipher.NeedsReEncryptionAsync(method.AccountDetailsCiphertext, cancellationToken))
            {
                continue;
            }

            var context = PaymentMethodMapping.EncryptionContext(method.Id);
            var plaintext = await cipher.DecryptAsync(method.AccountDetailsCiphertext, context, cancellationToken);
            method.ReplaceCiphertext(await cipher.EncryptAsync(plaintext, context, cancellationToken), _clock.UtcNow);
            processed++;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return processed;
    }
}
