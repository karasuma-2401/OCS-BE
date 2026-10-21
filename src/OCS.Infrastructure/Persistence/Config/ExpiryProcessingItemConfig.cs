using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence.Config;

public sealed class ExpiryProcessingItemConfig : IEntityTypeConfiguration<ExpiryProcessingItem>
{
    public void Configure(EntityTypeBuilder<ExpiryProcessingItem> builder)
    {
        builder.ToTable("CT_XULY");
        builder.HasKey(x => new { x.ExpiryProcessingId, x.ProductId });
        builder.HasOne(x => x.ExpiryProcessing).WithMany(x => x.Items).HasForeignKey(x => x.ExpiryProcessingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ProductId).IsUnique();
    }
}
