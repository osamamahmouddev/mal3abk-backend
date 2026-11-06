namespace Malabak.SharedKernel.Contracts;

public sealed record VenueComplaintDto(
    Guid TicketId, string Number, string Subject, string Status, string Priority, Guid RequesterId, DateTime CreatedAtUtc, string? Resolution);

/// <summary>Complaints about a venue, raised as support tickets (TASK-706) and shown on the admin venue page (TASK-702).</summary>
public interface IVenueComplaintsReader
{
    Task<IReadOnlyList<VenueComplaintDto>> GetComplaintsAsync(Guid venueId, int take, CancellationToken cancellationToken = default);
}
