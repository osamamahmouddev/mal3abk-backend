using Malabak.Modules.Identity.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Malabak.Modules.Identity.Infrastructure.Configurations;

public sealed class CrmContactConfiguration : IEntityTypeConfiguration<CrmContact>
{
    public void Configure(EntityTypeBuilder<CrmContact> builder)
    {
        builder.ToTable("crm_contacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Name).HasMaxLength(CrmContact.MaxNameLength).IsRequired();
        builder.Property(c => c.VenueName).HasMaxLength(CrmContact.MaxVenueNameLength);
        builder.Property(c => c.Area).HasMaxLength(CrmContact.MaxAreaLength);
        builder.Property(c => c.Region).HasMaxLength(CrmContact.MaxRegionLength);
        builder.Property(c => c.LostReason).HasMaxLength(CrmContact.MaxLostReasonLength);
        builder.Property(c => c.PhoneEncrypted).IsRequired();
        builder.Property(c => c.PhoneBlindIndex).HasMaxLength(64).IsRequired();
        builder.Property(c => c.Stage).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Source).HasConversion<string>().HasMaxLength(20);

        // One contact per phone number, so two people never chase the same venue owner separately.
        builder.HasIndex(c => c.PhoneBlindIndex).IsUnique();
        builder.HasIndex(c => new { c.Stage, c.NextFollowUpAtUtc });
        builder.HasIndex(c => c.Region);

        // Contacts in the trash are hidden everywhere; the trash and restore read them explicitly.
        builder.HasQueryFilter(c => !c.IsDeleted);

        builder.HasMany(c => c.Notes).WithOne().HasForeignKey(n => n.ContactId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Notes).HasField("_notes").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public sealed class CrmNoteConfiguration : IEntityTypeConfiguration<CrmNote>
{
    public void Configure(EntityTypeBuilder<CrmNote> builder)
    {
        builder.ToTable("crm_notes");
        builder.HasKey(n => n.Id);
        builder.Property(n => n.Id).ValueGeneratedNever();
        builder.Property(n => n.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(n => n.Text).HasMaxLength(CrmNote.MaxTextLength).IsRequired();
        builder.HasIndex(n => new { n.ContactId, n.CreatedAtUtc });
    }
}
