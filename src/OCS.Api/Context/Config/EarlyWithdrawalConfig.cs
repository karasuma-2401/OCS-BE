using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class EarlyWithdrawalConfig : IEntityTypeConfiguration<EarlyWithdrawal>
{
    public void Configure(EntityTypeBuilder<EarlyWithdrawal> builder)
    {
        builder.ToTable("EarlyWithdrawals", table =>
        {
            table.HasCheckConstraint("CK_EarlyWithdrawals_Status", "\"Status\" IN ('Pending', 'Approved', 'Rejected', 'Cancelled', 'Completed', 'Legacy')");
            table.HasCheckConstraint("CK_EarlyWithdrawals_TotalPenalty", "\"TotalPenaltyAmount\" >= 0");
            table.HasCheckConstraint(
                "CK_EarlyWithdrawals_PaymentStatus",
                "\"PaymentStatus\" IN ('Unpaid', 'Partial', 'Paid')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Status).HasMaxLength(20);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.Property(x => x.DecisionNote).HasMaxLength(1000);
        builder.Property(x => x.HandoverEvidence).HasMaxLength(1000);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.CustomerId, x.RequestedAt });
        builder.HasOne<EmployeeProfile>().WithMany().HasForeignKey(x => x.PaidByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmployeeProfile>().WithMany().HasForeignKey(x => x.HandedOverByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.TotalPenaltyAmount).HasPrecision(18, 2);
        builder.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(20);
        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ProcessedByEmployee).WithMany().HasForeignKey(x => x.ProcessedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
