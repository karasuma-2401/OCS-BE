using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence.Config;

public sealed class FinancialTransactionConfig : IEntityTypeConfiguration<FinancialTransaction>
{
    public void Configure(EntityTypeBuilder<FinancialTransaction> builder)
    {
        builder.ToTable("GIAODICHTAICHINH", table =>
        {
            table.HasCheckConstraint("CK_GIAODICHTAICHINH_Amount", "\"Amount\" >= 0");
            table.HasCheckConstraint(
                "CK_GIAODICHTAICHINH_Enums",
                "\"Type\" IN ('Income', 'Expense', 'CashFloat', 'BankDeposit') " +
                "AND \"PaymentMethod\" IN ('Cash', 'VietQr', 'BankTransfer') " +
                "AND \"Status\" IN ('Pending', 'Success', 'Failed', 'Refunded')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Amount).HasPrecision(18, 2);
        builder.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.BankTransactionReference).HasMaxLength(100);
        builder.Property(x => x.ProofUrl).HasMaxLength(2048);
        builder.Property(x => x.OccurredAt).HasColumnType("timestamp with time zone");

        builder.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.SaleInvoice).WithMany().HasForeignKey(x => x.SaleInvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Payout).WithMany().HasForeignKey(x => x.PayoutId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Buyout).WithMany().HasForeignKey(x => x.BuyoutId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.EarlyWithdrawal).WithMany().HasForeignKey(x => x.EarlyWithdrawalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.PerformedByEmployee).WithMany().HasForeignKey(x => x.PerformedByEmployeeId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.Status, x.OccurredAt });
    }
}
