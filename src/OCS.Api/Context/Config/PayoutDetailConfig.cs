using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class PayoutDetailConfig : IEntityTypeConfiguration<PayoutDetail>
{
    public void Configure(EntityTypeBuilder<PayoutDetail> builder)
    {
        builder.ToTable("CT_GIAINGAN", table =>
        {
            table.HasCheckConstraint(
                "CK_CT_GIAINGAN_Type",
                "\"Type\" IN ('Commission', 'Compensation', 'ServiceFee')");
            table.HasCheckConstraint(
                "CK_CT_GIAINGAN_Source",
                "NOT (\"ProductId\" IS NOT NULL AND \"IncidentReportId\" IS NOT NULL)");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Description).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Amount).HasPrecision(18, 2);

        builder.HasOne(x => x.Payout)
            .WithMany(x => x.Details)
            .HasForeignKey(x => x.PayoutId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Product)
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.IncidentReport)
            .WithMany()
            .HasForeignKey(x => x.IncidentReportId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
