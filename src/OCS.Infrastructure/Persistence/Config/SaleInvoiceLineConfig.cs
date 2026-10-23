using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence.Config;

public sealed class SaleInvoiceLineConfig : IEntityTypeConfiguration<SaleInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SaleInvoiceLine> builder)
    {
        builder.ToTable("CT_HOADON", table =>
            table.HasCheckConstraint("CK_CT_HOADON_SalePrice", "\"SalePrice\" >= 0"));

        builder.HasKey(x => new { x.SaleInvoiceId, x.ProductId });
        builder.Property(x => x.SalePrice).HasPrecision(18, 2);

        builder.HasOne(x => x.SaleInvoice)
            .WithMany(x => x.Lines)
            .HasForeignKey(x => x.SaleInvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Product)
            .WithOne(x => x.SaleInvoiceLine)
            .HasForeignKey<SaleInvoiceLine>(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
