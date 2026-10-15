using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Domain.Entities;

namespace OCS.Infrastructure.Persistence.Config;

public sealed class ProductImageConfig
    : IEntityTypeConfiguration<ProductImage>
{
    public void Configure(EntityTypeBuilder<ProductImage> builder)
    {
        builder.ToTable("ANH_SANPHAM", table =>
        {
            table.HasCheckConstraint(
                "CK_ANH_SANPHAM_ImageType",
                """
                "ImageType" IN ('Cover', 'Detail', 'Incident')
                """);

            table.HasCheckConstraint(
                "CK_ANH_SANPHAM_SortOrder",
                """
                "SortOrder" >= 0
                """);

            table.HasCheckConstraint(
                "CK_ANH_SANPHAM_Url",
                """
                length(btrim("Url")) > 0
                """);
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Url)
            .HasMaxLength(2048)
            .IsRequired();

        builder.Property(x => x.ImageType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.HasOne(x => x.Product)
            .WithMany(x => x.Images)
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.ProductId,
            x.SortOrder
        });

        builder.HasIndex(x => x.ProductId)
            .IsUnique()
            .HasFilter("\"ImageType\" = 'Cover'");
    }
}