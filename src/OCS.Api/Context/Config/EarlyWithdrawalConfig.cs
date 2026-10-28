using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class EarlyWithdrawalConfig : IEntityTypeConfiguration<EarlyWithdrawal>
{
    public void Configure(EntityTypeBuilder<EarlyWithdrawal> builder)
    {
        builder.ToTable("PHIEURUTHANGSOM", table =>
        {
            table.HasCheckConstraint("CK_PHIEURUTHANGSOM_TotalPenalty", "\"TotalPenaltyAmount\" >= 0");
            table.HasCheckConstraint(
                "CK_PHIEURUTHANGSOM_PaymentStatus",
                "\"PaymentStatus\" IN ('Unpaid', 'Partial', 'Paid')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TotalPenaltyAmount).HasPrecision(18, 2);
        builder.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(20);
        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProcessedByEmployee).WithMany().HasForeignKey(x => x.ProcessedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
