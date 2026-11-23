using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class DonationReceiptConfig : IEntityTypeConfiguration<DonationReceipt>
{
    public void Configure(EntityTypeBuilder<DonationReceipt> builder)
    {
        builder.ToTable("DonationReceipts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.ReceiptNumber).HasMaxLength(40);
        builder.HasIndex(x => x.ReceiptNumber).IsUnique();
        builder.Property(x => x.ContractNumber).HasMaxLength(100);
        builder.Property(x => x.CustomerName).HasMaxLength(200);
        builder.Property(x => x.RecipientOrganization).HasMaxLength(200);
        builder.Property(x => x.Evidence).HasMaxLength(1000);
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.Property(x => x.ProductsJson).HasColumnType("jsonb");
        builder.HasOne(x => x.ExpiryProcessing).WithMany().HasForeignKey(x => x.ExpiryProcessingId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<EmployeeProfile>().WithMany().HasForeignKey(x => x.HandledByEmployeeId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
