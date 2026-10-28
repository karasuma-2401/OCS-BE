using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class ProductConfig : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("SANPHAM", table =>
        {
            table.HasCheckConstraint(
                "CK_SANPHAM_Condition",
                """
                "ConditionPercentage" BETWEEN 0 AND 100
                """);

            table.HasCheckConstraint(
                "CK_SANPHAM_Prices",
                """
                "ListedPrice" >= 0
                AND (
                    "PurchasePrice" IS NULL
                    OR "PurchasePrice" >= 0
                )
                """);

            table.HasCheckConstraint(
                "CK_SANPHAM_Commission",
                """
                "AgreedCommissionRate" IS NULL
                OR "AgreedCommissionRate" BETWEEN 0 AND 100
                """);

            table.HasCheckConstraint(
                "CK_SANPHAM_Source",
                """
                (
                    "SourceType" = 'Consignment'
                    AND "ConsignmentContractId" IS NOT NULL
                    AND "BuyoutId" IS NULL
                    AND "PurchasePrice" IS NULL
                    AND "AgreedCommissionRate" IS NOT NULL
                    AND "AgreedBrandTier" IS NOT NULL
                )
                OR
                (
                    "SourceType" = 'Buyout'
                    AND "ConsignmentContractId" IS NULL
                    AND "BuyoutId" IS NOT NULL
                    AND "PurchasePrice" IS NOT NULL
                    AND "AgreedCommissionRate" IS NULL
                    AND "AgreedBrandTier" IS NULL
                )
                """);

            table.HasCheckConstraint(
                "CK_SANPHAM_Enums",
                """
                "Status" IN (
                    'PendingProcessing', 'OnSale', 'Sold',
                    'Expired', 'Donated', 'Returned',
                    'EarlyWithdrawn', 'Compensated', 'Cancelled'
                )
                AND (
                    "AgreedBrandTier" IS NULL
                    OR "AgreedBrandTier" IN (
                        'NoBrand', 'LocalBrand',
                        'Commercial', 'Luxury'
                    )
                )
                """);
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Barcode)
            .HasMaxLength(100)
            .IsRequired();

        builder.HasIndex(x => x.Barcode)
            .IsUnique();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Size)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.Description)
            .HasMaxLength(2000);

        builder.Property(x => x.ListedPrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.PurchasePrice)
            .HasPrecision(18, 2);

        builder.Property(x => x.AgreedCommissionRate)
            .HasPrecision(5, 2);

        builder.Property(x => x.SourceType)
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(x => x.AgreedBrandTier)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.Property(x => x.Status)
            .HasConversion<string>()
            .HasMaxLength(30);

        builder.HasOne(x => x.Brand)
            .WithMany()
            .HasForeignKey(x => x.BrandId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Category)
            .WithMany()
            .HasForeignKey(x => x.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.StorageLocation)
            .WithMany()
            .HasForeignKey(x => x.StorageLocationId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.ConsignmentContract)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.ConsignmentContractId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Buyout)
            .WithMany(x => x.Products)
            .HasForeignKey(x => x.BuyoutId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new
        {
            x.Status,
            x.CategoryId
        });

        builder.HasIndex(x => new
        {
            x.ConsignmentContractId,
            x.Status
        });
    }
}