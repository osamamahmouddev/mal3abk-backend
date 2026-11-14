using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Security;
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Malabak.Modules.Venues.Application.PaymentMethods;

/// <summary>Decrypts a venue's active payment methods for a booking's player, auditing each reveal.</summary>
public sealed class VenuePaymentDirectory : IVenuePaymentDirectory
{
    private readonly VenuesDbContext _dbContext;
    private readonly IFieldCipher _cipher;
    private readonly IVenueAuditLogger _auditLogger;
    private readonly ILogger<VenuePaymentDirectory> _logger;

    public VenuePaymentDirectory(VenuesDbContext dbContext, IFieldCipher cipher, IVenueAuditLogger auditLogger, ILogger<VenuePaymentDirectory>? logger = null)
    {
        _dbContext = dbContext;
        _cipher = cipher;
        _auditLogger = auditLogger;
        _logger = logger ?? NullLogger<VenuePaymentDirectory>.Instance;
    }

    public async Task<IReadOnlyList<PayableVenuePaymentMethod>> RevealForBookingAsync(
        Guid venueId, Guid bookingId, Guid playerId, CancellationToken cancellationToken = default)
    {
        var methods = await _dbContext.PaymentMethods.IgnoreQueryFilters().AsNoTracking()
            .Where(m => m.VenueId == venueId && m.IsActive && !m.IsDeleted)
            .OrderBy(m => m.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        var revealed = new List<PayableVenuePaymentMethod>(methods.Count);
        foreach (var method in methods)
        {
            string details;
            try
            {
                details = await _cipher.DecryptAsync(method.AccountDetailsCiphertext, PaymentMethodMapping.EncryptionContext(method.Id), cancellationToken);
            }
            catch (CryptographicException ex)
            {
                // A method saved under keys this server no longer has (rotated away, or a development restart without
                // configured keys) is left out instead of failing the whole list; the owner has to enter it again.
                _logger.LogError(ex, "Payment method {PaymentMethodId} of venue {VenueId} could not be decrypted and was skipped", method.Id, method.VenueId);
                continue;
            }

            await _auditLogger.LogAsync(
                "PaymentMethod.DetailsRevealedToPlayer", nameof(VenuePaymentMethod), method.Id, playerId, new { method.VenueId, BookingId = bookingId }, cancellationToken);
            revealed.Add(new PayableVenuePaymentMethod(method.Id, method.MethodType, method.RecipientName, details, method.Instructions));
        }

        return revealed;
    }
}
