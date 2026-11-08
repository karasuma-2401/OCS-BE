using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class FinancialTransactionConfig : IEntityTypeConfiguration<FinancialTransaction>
{
    public void Configure(EntityTypeBuilder<FinancialTransaction> builder)
    {
        builder.ToTable("FinancialTransactions", table =>
        {
            table.HasCheckConstraint("CK_FinancialTransactions_Amount", "\"Amount\" >= 0");
            table.HasCheckConstraint(
                "CK_FinancialTransactions_Enums",
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
        builder.Property(x => x.PaymentReference).HasMaxLength(64);
        builder.Property(x => x.ExpiresAt).HasColumnType("timestamp with time zone");
        builder.Property(x => x.QrCodeUrl).HasMaxLength(2048);
        builder.Property(x => x.QrPayload).HasMaxLength(1024);
        builder.Property(x => x.ProviderName).HasMaxLength(50);
        builder.Property(x => x.BankCode).HasMaxLength(50);
        builder.Property(x => x.BankAccountNumber).HasMaxLength(50);
        builder.Property(x => x.BankAccountHolderName).HasMaxLength(200);
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
        builder.HasIndex(x => x.PaymentReference)
            .IsUnique()
            .HasFilter("\"PaymentReference\" IS NOT NULL");
    }
}
