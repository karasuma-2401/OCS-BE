using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class BrandConfig : IEntityTypeConfiguration<Brand>
{
    public void Configure(EntityTypeBuilder<Brand> builder)
    {
        builder.ToTable("THUONGHIEU", table =>
        {
            table.HasCheckConstraint(
                "CK_THUONGHIEU_CommissionRate",
                """
                "DefaultCommissionRate" BETWEEN 0 AND 100
                """);

            table.HasCheckConstraint(
                "CK_THUONGHIEU_Tier",
                """
                "Tier" IN (
                    'NoBrand',
                    'LocalBrand',
                    'Commercial',
                    'Luxury'
                )
                """);
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Id)
            .ValueGeneratedNever();

        builder.Property(x => x.Name)
            .HasMaxLength(200)
            .IsRequired();

        builder.Property(x => x.Tier)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(x => x.DefaultCommissionRate)
            .HasPrecision(5, 2)
            .IsRequired();

        builder.HasIndex(x => x.Name)
            .IsUnique();
    }
}