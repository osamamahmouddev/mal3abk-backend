using Malabak.Modules.Venues.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Venues.Infrastructure.Configurations;

public sealed class VenueApprovalEventConfiguration : IEntityTypeConfiguration<VenueApprovalEvent>
{
    public void Configure(EntityTypeBuilder<VenueApprovalEvent> builder)
    {
        // Append-only history of the approval conversation.
        builder.ToTable("venue_approval_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.VenueId).IsRequired();
        builder.Property(e => e.Action).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.FromStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.ToStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.ActorUserId).IsRequired();
        builder.Property(e => e.Message).HasMaxLength(VenueApprovalEvent.MaxMessageLength);
        builder.Property(e => e.CreatedAtUtc).IsRequired();
        builder.HasIndex(e => new { e.VenueId, e.CreatedAtUtc });
    }
}
