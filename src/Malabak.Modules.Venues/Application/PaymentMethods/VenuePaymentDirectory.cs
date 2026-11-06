using Malabak.Modules.Venues.Application.Common;
using Malabak.Modules.Venues.Domain;
using Malabak.Modules.Venues.Infrastructure;
using Malabak.SharedKernel.Contracts;
using Malabak.SharedKernel.Security;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Venues.Application.PaymentMethods;

/// <summary>Decrypts a venue's active payment methods for a booking's player, auditing each reveal.</summary>
public sealed class VenuePaymentDirectory : IVenuePaymentDirectory
{
    private readonly VenuesDbContext _dbContext;
    private readonly IFieldCipher _cipher;
    private readonly IVenueAuditLogger _auditLogger;

    public VenuePaymentDirectory(VenuesDbContext dbContext, IFieldCipher cipher, IVenueAuditLogger auditLogger)
    {
        _dbContext = dbContext;
        _cipher = cipher;
        _auditLogger = auditLogger;
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
            var details = await _cipher.DecryptAsync(method.AccountDetailsCiphertext, PaymentMethodMapping.EncryptionContext(method.Id), cancellationToken);
            await _auditLogger.LogAsync(
                "PaymentMethod.DetailsRevealedToPlayer", nameof(VenuePaymentMethod), method.Id, playerId, new { method.VenueId, BookingId = bookingId }, cancellationToken);
            revealed.Add(new PayableVenuePaymentMethod(method.Id, method.MethodType, method.RecipientName, details, method.Instructions));
        }

        return revealed;
    }
}
