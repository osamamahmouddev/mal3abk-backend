using Malabak.Modules.Identity.Application.Crm;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Identity.Infrastructure;

/// <summary>Re-encrypts CRM contact phone numbers still under a retired data key (key rotation).</summary>
public sealed class CrmPhoneReEncryptionTarget : IReEncryptionTarget
{
    private const int _pageSize = 500;

    private readonly IdentityDbContext _dbContext;

    public CrmPhoneReEncryptionTarget(IdentityDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "identity.crm_contacts";

    public async Task<int> ReEncryptBatchAsync(IFieldCipher cipher, int batchSize, CancellationToken cancellationToken = default)
    {
        // The key id lives inside the ciphertext, so rows are checked in memory, a page at a time.
        var processed = 0;
        var offset = 0;
        while (processed < batchSize)
        {
            var page = await _dbContext.CrmContacts.OrderBy(c => c.Id).Skip(offset).Take(_pageSize).ToListAsync(cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var contact in page)
            {
                if (processed >= batchSize || !await cipher.NeedsReEncryptionAsync(contact.PhoneEncrypted, cancellationToken))
                {
                    continue;
                }

                var context = CrmService.PhoneContext(contact.Id);
                var phone = await cipher.DecryptAsync(contact.PhoneEncrypted, context, cancellationToken);
                contact.ReplacePhone(await cipher.EncryptAsync(phone, context, cancellationToken), contact.PhoneBlindIndex, contact.UpdatedAtUtc);
                processed++;
            }

            offset += page.Count;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return processed;
    }
}
