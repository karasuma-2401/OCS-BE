using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class PayoutConfig : IEntityTypeConfiguration<Payout>
{
    public void Configure(EntityTypeBuilder<Payout> builder)
    {
        builder.ToTable("Payouts", table =>
        {
            table.HasCheckConstraint("CK_Payouts_Total", "\"TotalAmount\" >= 0");
            table.HasCheckConstraint(
                "CK_Payouts_Status",
                "\"Status\" IN ('Pending', 'Approved', 'Transferred', 'Completed', 'Rejected')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.PayoutNumber).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.PayoutNumber).IsUnique();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.ApprovedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.RejectedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.RejectionReason).HasMaxLength(1000);
        builder.Property(x => x.BankCode).HasMaxLength(50);
        builder.Property(x => x.BankAccountNumber).HasMaxLength(50);
        builder.Property(x => x.BankAccountHolder).HasMaxLength(200);
        builder.Property(x => x.TransferredAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CustomerConfirmedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.CompletedAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.ProofUrl).HasMaxLength(2048);
        builder.Property(x => x.BankTransactionReference).HasMaxLength(100);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20).IsConcurrencyToken();

        builder.HasOne(x => x.ConsignmentContract)
            .WithMany(x => x.Payouts)
            .HasForeignKey(x => x.ConsignmentContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.CreatedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.CreatedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ApprovedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.ApprovedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.RejectedByEmployee)
            .WithMany()
            .HasForeignKey(x => x.RejectedByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
