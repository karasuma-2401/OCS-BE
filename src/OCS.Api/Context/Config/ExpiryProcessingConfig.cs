using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class ExpiryProcessingConfig : IEntityTypeConfiguration<ExpiryProcessing>
{
    public void Configure(EntityTypeBuilder<ExpiryProcessing> builder)
    {
        builder.ToTable("ExpiryProcessings", table =>
        {
            table.HasCheckConstraint(
                "CK_ExpiryProcessings_Enums",
                "\"Type\" IN ('Donate', 'Return') " +
                "AND \"Status\" IN ('Pending', 'Completed', 'OverdueDonated')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CollectionDeadline).HasColumnType("timestamp with time zone");
        builder.Property(x => x.ConfirmedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.RecipientOrganization).HasMaxLength(200);
        builder.Property(x => x.Evidence).HasMaxLength(1000);
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.Property(x => x.UnclaimedBasis).HasMaxLength(1000);
        builder.HasOne<EmployeeProfile>().WithMany().HasForeignKey(x => x.UnclaimedApprovedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProcessedByEmployee).WithMany().HasForeignKey(x => x.ProcessedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
