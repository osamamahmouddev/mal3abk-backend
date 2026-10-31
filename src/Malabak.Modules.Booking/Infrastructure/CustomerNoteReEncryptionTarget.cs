using Malabak.Modules.Booking.Application.Customers;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Booking.Infrastructure;

/// <summary>Re-encrypts venue customer notes still under a retired data key (key rotation).</summary>
public sealed class CustomerNoteReEncryptionTarget : IReEncryptionTarget
{
    private const int _pageSize = 500;

    private readonly BookingDbContext _dbContext;

    public CustomerNoteReEncryptionTarget(BookingDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public string Name => "booking.venue_customer_notes";

    public async Task<int> ReEncryptBatchAsync(IFieldCipher cipher, int batchSize, CancellationToken cancellationToken = default)
    {
        // The key id lives inside the ciphertext, so rows are checked in memory, a page at a time (deleted notes included).
        var processed = 0;
        var offset = 0;
        while (processed < batchSize)
        {
            var page = await _dbContext.CustomerNotes
                .OrderBy(n => n.Id)
                .Skip(offset)
                .Take(_pageSize)
                .ToListAsync(cancellationToken);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var note in page)
            {
                if (processed >= batchSize || !await cipher.NeedsReEncryptionAsync(note.TextCiphertext, cancellationToken))
                {
                    continue;
                }

                var context = VenueCustomerService.NoteContext(note.Id);
                var plaintext = await cipher.DecryptAsync(note.TextCiphertext, context, cancellationToken);
                note.SetCiphertext(await cipher.EncryptAsync(plaintext, context, cancellationToken));
                processed++;
            }

            offset += page.Count;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return processed;
    }
}
