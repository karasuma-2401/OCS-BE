using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence.Config;

public sealed class SaleInvoiceConfig : IEntityTypeConfiguration<SaleInvoice>
{
    public void Configure(EntityTypeBuilder<SaleInvoice> builder)
    {
        builder.ToTable("HOADONBAN", table =>
        {
            table.HasCheckConstraint("CK_HOADONBAN_Total", "\"TotalAmount\" >= 0");
            table.HasCheckConstraint(
                "CK_HOADONBAN_PaymentStatus",
                "\"PaymentStatus\" IN ('Unpaid', 'Partial', 'Paid')");
        });

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.InvoiceNumber).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.Property(x => x.TotalAmount).HasPrecision(18, 2);
        builder.Property(x => x.PaymentStatus).HasConversion<string>().HasMaxLength(20);

        builder.HasOne(x => x.SoldByEmployee)
            .WithMany()
            .HasForeignKey(x => x.SoldByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
