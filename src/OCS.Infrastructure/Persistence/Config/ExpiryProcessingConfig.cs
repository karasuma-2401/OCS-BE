using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence.Config;

public sealed class ExpiryProcessingConfig : IEntityTypeConfiguration<ExpiryProcessing>
{
    public void Configure(EntityTypeBuilder<ExpiryProcessing> builder)
    {
        builder.ToTable("PHIEUXULYHETHAN", table =>
        {
            table.HasCheckConstraint(
                "CK_PHIEUXULYHETHAN_Enums",
                "\"Type\" IN ('Donate', 'Return') " +
                "AND \"Status\" IN ('Pending', 'Completed', 'OverdueDonated')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CollectionDeadline).HasColumnType("timestamp with time zone");
        builder.Property(x => x.ConfirmedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne(x => x.ProcessedByEmployee).WithMany().HasForeignKey(x => x.ProcessedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
