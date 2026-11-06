using Malabak.Modules.Support.Domain;
using Microsoft.EntityFrameworkCore;

namespace Malabak.Modules.Support.Infrastructure;

public sealed class SupportDbContext : DbContext
{
    public const string Schema = SupportModule.Schema;

    public DbSet<SupportTicket> Tickets => Set<SupportTicket>();
    public DbSet<TicketMessage> Messages => Set<TicketMessage>();

    public SupportDbContext(DbContextOptions<SupportDbContext> options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<SupportTicket>(b =>
        {
            b.ToTable("tickets");
            b.HasKey(t => t.Id);
            b.Property(t => t.Id).ValueGeneratedNever();
            b.Property(t => t.Number).HasMaxLength(12).IsRequired();
            b.HasIndex(t => t.Number).IsUnique();
            b.Property(t => t.Subject).HasMaxLength(SupportTicket.MaxSubjectLength).IsRequired();
            b.Property(t => t.ResolutionReason).HasMaxLength(SupportTicket.MaxReasonLength);
            b.Property(t => t.RequesterKind).HasConversion<string>().HasMaxLength(10);
            b.Property(t => t.Category).HasConversion<string>().HasMaxLength(20);
            b.Property(t => t.Priority).HasConversion<string>().HasMaxLength(10);
            b.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(t => new { t.RequesterId, t.CreatedAtUtc });
            b.HasIndex(t => new { t.Status, t.Priority, t.LastActivityAtUtc });
            b.HasIndex(t => t.AssigneeId);
            b.HasIndex(t => new { t.RelatedVenueId, t.Category });
            b.HasIndex(t => t.RelatedBookingId);
            b.HasMany(t => t.Messages).WithOne().HasForeignKey(m => m.TicketId);
            b.Navigation(t => t.Messages).UsePropertyAccessMode(PropertyAccessMode.Field);
        });

        modelBuilder.Entity<TicketMessage>(b =>
        {
            b.ToTable("ticket_messages");
            b.HasKey(m => m.Id);
            b.Property(m => m.Id).ValueGeneratedNever();
            b.Property(m => m.Body).HasMaxLength(SupportTicket.MaxBodyLength).IsRequired();
            b.Property(m => m.AuthorKind).HasConversion<string>().HasMaxLength(10);
            b.HasIndex(m => new { m.TicketId, m.CreatedAtUtc });
        });
    }
}
