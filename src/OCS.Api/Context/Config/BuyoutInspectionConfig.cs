using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class BuyoutInspectionConfig : IEntityTypeConfiguration<BuyoutInspection>
{
    public void Configure(EntityTypeBuilder<BuyoutInspection> builder)
    {
        builder.ToTable("BuyoutInspections", t => t.HasCheckConstraint("CK_BuyoutInspections_Condition", "\"ConditionPercentage\" BETWEEN 0 AND 100"));
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id).ValueGeneratedNever();
        builder.Property(i => i.Name).HasMaxLength(200).IsRequired();
        builder.Property(i => i.Size).HasMaxLength(30).IsRequired();
        builder.Property(i => i.Description).HasMaxLength(2000);
        builder.Property(i => i.InspectionNotes).HasMaxLength(1000).IsRequired();
        builder.HasOne(i => i.Customer).WithMany().HasForeignKey(i => i.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.InspectedByEmployee).WithMany().HasForeignKey(i => i.InspectedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Brand).WithMany().HasForeignKey(i => i.BrandId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Category).WithMany().HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class BuyoutProposalItemConfig : IEntityTypeConfiguration<BuyoutProposalItem>
{
    public void Configure(EntityTypeBuilder<BuyoutProposalItem> builder)
    {
        builder.ToTable("BuyoutProposalItems", t => t.HasCheckConstraint("CK_BuyoutProposalItems_Price", "\"ListedPrice\" >= 0"));
        builder.HasKey(i => i.BuyoutId);
        builder.Property(i => i.ListedPrice).HasPrecision(18, 2);
        builder.HasOne(i => i.Buyout).WithOne().HasForeignKey<BuyoutProposalItem>(i => i.BuyoutId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(i => i.Inspection).WithMany().HasForeignKey(i => i.InspectionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(i => i.InspectionId).IsUnique().HasFilter("\"IsActive\" = TRUE");
    }
}
