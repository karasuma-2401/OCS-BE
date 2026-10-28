using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class IncidentReportConfig : IEntityTypeConfiguration<IncidentReport>
{
    public void Configure(EntityTypeBuilder<IncidentReport> builder)
    {
        builder.ToTable("BIENBANSUCO", table =>
        {
            table.HasCheckConstraint("CK_BIENBANSUCO_Compensation", "\"CompensationAmount\" >= 0");
            table.HasCheckConstraint(
                "CK_BIENBANSUCO_Enums",
                "\"Type\" IN ('Lost', 'Damaged', 'TagSwapped') " +
                "AND \"Status\" IN ('Pending', 'Approved', 'Compensated')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.CompensationAmount).HasPrecision(18, 2);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CreatedByEmployee).WithMany().HasForeignKey(x => x.CreatedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ApprovedByEmployee).WithMany().HasForeignKey(x => x.ApprovedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ProductId).IsUnique();
    }
}
