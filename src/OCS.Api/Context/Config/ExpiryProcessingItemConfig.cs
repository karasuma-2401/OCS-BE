using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class ExpiryProcessingItemConfig : IEntityTypeConfiguration<ExpiryProcessingItem>
{
    public void Configure(EntityTypeBuilder<ExpiryProcessingItem> builder)
    {
        builder.ToTable("ExpiryProcessingItems");
        builder.HasKey(x => new { x.ExpiryProcessingId, x.ProductId });
        builder.HasOne(x => x.ExpiryProcessing).WithMany(x => x.Items).HasForeignKey(x => x.ExpiryProcessingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ProductId).IsUnique();
        builder.Property(x => x.RecipientOrganization).HasMaxLength(200);
        builder.Property(x => x.Evidence).HasMaxLength(1000);
        builder.Property(x => x.Note).HasMaxLength(1000);
        builder.HasOne<EmployeeProfile>().WithMany().HasForeignKey(x => x.HandledByEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
