namespace Malabak.Modules.Venues.Application.Common;

public interface IBookingConflictChecker
{
    Task<bool> HasActiveBookingsAsync(Guid pitchId, DateTime startUtc, DateTime endUtc, CancellationToken cancellationToken = default);
}
