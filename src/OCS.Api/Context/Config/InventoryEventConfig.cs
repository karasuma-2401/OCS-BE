using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OCS.Api.Entities;

namespace OCS.Api.Context.Config;

public sealed class InventoryEventConfig : IEntityTypeConfiguration<InventoryEvent>
{
    public void Configure(EntityTypeBuilder<InventoryEvent> builder)
    {
        builder.ToTable("InventoryEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.Action).HasMaxLength(40).IsRequired();
        builder.Property(x => x.PreviousValue).HasMaxLength(1000);
        builder.Property(x => x.NewValue).HasMaxLength(1000);
        builder.Property(x => x.Reason).HasMaxLength(1000);
        builder.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Account>().WithMany().HasForeignKey(x => x.ActorAccountId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(x => new { x.ProductId, x.OccurredAt });
    }
}
