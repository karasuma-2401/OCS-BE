using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class EarlyWithdrawalItemConfig : IEntityTypeConfiguration<EarlyWithdrawalItem>
{
    public void Configure(EntityTypeBuilder<EarlyWithdrawalItem> builder)
    {
        builder.ToTable("CT_RUTSOM", table =>
        {
            table.HasCheckConstraint("CK_CT_RUTSOM_ListedPrice", "\"ListedPrice\" >= 0");
            table.HasCheckConstraint("CK_CT_RUTSOM_Penalty", "\"PenaltyAmount\" >= 0");
        });

        builder.HasKey(x => new { x.EarlyWithdrawalId, x.ProductId });
        builder.Property(x => x.ListedPrice).HasPrecision(18, 2);
        builder.Property(x => x.PenaltyAmount).HasPrecision(18, 2);
        builder.HasOne(x => x.EarlyWithdrawal).WithMany(x => x.Items).HasForeignKey(x => x.EarlyWithdrawalId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => x.ProductId).IsUnique();
    }
}
