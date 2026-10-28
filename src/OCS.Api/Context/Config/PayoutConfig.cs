using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class PayoutConfig : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> builder)
    {
        builder.ToTable("PHIEUGIAINGAN", table =>
        {
            table.HasCheckConstraint("CK_PHIEUGIAINGAN_Total", "\"TotalAmount\" >= 0");
            table.HasCheckConstraint(
                "CK_PHIEUGIAINGAN_Status",
                "\"Status\" IN ('Pending', 'Transferred', 'Completed')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PayoutNumber).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.PayoutNumber).IsUnique();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.ProofUrl).HasMaxLength(2048);
        builder.Property(x => x.BankTransactionReference).HasMaxLength(100);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(x => x.ConsignmentContract)
            .WithMany(x => x.Payouts)
            .HasForeignKey(x => x.ConsignmentContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ApprovedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.ApprovedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
